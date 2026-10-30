using LumiCine.Application.Discovery;
using LumiCine.Domain.Catalog;
using LumiCine.Domain.Scheduling;
using LumiCine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace LumiCine.Infrastructure.Discovery;

/// <summary>
/// Retrieval stage of discovery: loads filtered movie candidates, ranks them by cosine
/// similarity over movie embeddings fused with keyword scores, and returns the top movies
/// for the conversation turn. Showtime availability is resolved on top of this ranking.
/// </summary>
public sealed class DiscoverySearchService(
    LumiCineDbContext db,
    TimeProvider clock,
    GeminiAiClients clients,
    IOptions<GeminiOptions> gemini,
    IOptions<DiscoveryConversationOptions> conversation) : IDiscoverySearchService
{
    private const int CandidateCap = 5000;

    public async Task<DiscoverySearchOutput> SearchAsync(DiscoverySearchInput input, CancellationToken cancellationToken)
    {
        var now = UtcNow;
        var filters = input.Filters;
        var (windowStart, windowEnd) = DiscoveryTime.ResolveWindowUtc(filters.DateFrom, filters.DateTo, now);
        var warnings = new List<string>();
        var isDegraded = false;

        var (candidates, capped) = await LoadCandidatesAsync(filters, windowStart, windowEnd, now, cancellationToken);
        if (capped)
        {
            warnings.Add("Số phim ứng viên vượt giới hạn, đã chuyển sang tìm từ khóa.");
            isDegraded = true;
        }

        var keywordQuery = string.Join(' ', new[] { filters.TopicText, filters.MovieNameText }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var hasTextQuery = keywordQuery.Length > 0;

        float[]? queryVector = null;
        if (hasTextQuery && !capped && candidates.Count > 0 && clients.IsConfigured)
        {
            queryVector = await TryEmbedQueryAsync(keywordQuery, warnings, cancellationToken);
            isDegraded |= queryVector is null;
        }

        var semanticRanking = queryVector is null
            ? null
            : await BuildSemanticRankingAsync(queryVector, candidates, cancellationToken);
        var keywordRanking = RankByKeyword(candidates, keywordQuery);

        string mode;
        IReadOnlyList<(Guid Id, double Score)> ranked;
        if (!hasTextQuery)
        {
            // Pure schedule search: relevance comes from showtimes, so keep candidate (Id) order.
            mode = DiscoveryModes.Schedule;
            ranked = candidates.Select(c => (c.Id, 0.0)).ToList();
        }
        else if (semanticRanking is not null)
        {
            mode = DiscoveryModes.Hybrid;
            ranked = DiscoveryVectorMath.ReciprocalRankFusion(
            [
                semanticRanking.Select(x => x.Id).ToArray(),
                keywordRanking.Select(x => x.Id).ToArray()
            ]);
        }
        else
        {
            mode = DiscoveryModes.Keyword;
            ranked = keywordRanking;
        }

        var byId = candidates.ToDictionary(c => c.Id);
        var results = new List<DiscoveryMovieResult>();
        foreach (var (movieId, score) in ranked)
        {
            if (results.Count >= conversation.Value.ResultMovieLimit)
                break;
            if (!byId.TryGetValue(movieId, out var candidate))
                continue;
            results.Add(new DiscoveryMovieResult(candidate.Id, candidate.Title, candidate.OriginalTitle,
                candidate.DurationMinutes, candidate.AgeRating, candidate.PosterUrl, candidate.GenreNames, [], score));
        }

        return new DiscoverySearchOutput(results, mode, isDegraded, warnings, now);
    }

    private async Task<(List<MovieCandidate> Candidates, bool Capped)> LoadCandidatesAsync(
        DiscoveryFilters filters, DateTime windowStart, DateTime windowEnd, DateTime now, CancellationToken ct)
    {
        var genreSlugs = filters.GenreSlugs
            .Select(s => s.Trim().ToLowerInvariant())
            .Where(s => s.Length > 0)
            .Distinct()
            .ToList();
        // Slugs are stored lowercase; contains filters mirror the catalog service's lowered terms.
        var nameLower = string.IsNullOrWhiteSpace(filters.MovieNameText) ? null : filters.MovieNameText.Trim().ToLower();
        var cityLower = string.IsNullOrWhiteSpace(filters.City) ? null : filters.City.Trim().ToLower();

        var qualifyingShowtimes = db.Showtimes.AsNoTracking()
            .Where(s => s.Status == ShowtimeStatus.Scheduled &&
                s.StartAtUtc >= windowStart && s.StartAtUtc >= now && s.StartAtUtc < windowEnd)
            .Join(db.Rooms.AsNoTracking().Where(r => r.IsActive), s => s.RoomId, r => r.Id, (s, r) => new { s, r })
            .Join(db.Cinemas.AsNoTracking().Where(c => c.IsActive), x => x.r.CinemaId, c => c.Id, (x, c) => new { x.s, x.r, c });
        if (filters.CinemaId is Guid cinemaId)
            qualifyingShowtimes = qualifyingShowtimes.Where(x => x.c.Id == cinemaId);
        if (cityLower is not null)
            qualifyingShowtimes = qualifyingShowtimes.Where(x => x.c.City.ToLower() == cityLower);

        var movies = db.Movies.AsNoTracking().Where(m => m.IsActive && qualifyingShowtimes.Any(s => s.s.MovieId == m.Id));
        if (filters.MaxDurationMinutes is int maxDuration)
            movies = movies.Where(m => m.DurationMinutes <= maxDuration);
        if (genreSlugs.Count > 0)
            movies = movies.Where(m => db.MovieGenres.Any(mg => mg.MovieId == m.Id &&
                db.Genres.Any(g => g.Id == mg.GenreId && genreSlugs.Contains(g.Slug))));
        if (nameLower is not null)
            movies = movies.Where(m => m.Title.ToLower().Contains(nameLower) ||
                (m.OriginalTitle != null && m.OriginalTitle.ToLower().Contains(nameLower)));

        var rows = await movies
            .OrderBy(m => m.Id)
            .Take(CandidateCap + 1)
            .Select(m => new { m.Id, m.Title, m.OriginalTitle, m.DurationMinutes, m.AgeRating, m.PosterUrl, m.Synopsis })
            .ToListAsync(ct);
        var capped = rows.Count > CandidateCap;
        if (capped)
            rows = rows.Take(CandidateCap).ToList();

        var ids = rows.Select(r => r.Id).ToList();
        var genreRows = await db.MovieGenres.AsNoTracking()
            .Join(db.Genres.AsNoTracking(), mg => mg.GenreId, g => g.Id, (mg, g) => new { mg.MovieId, g.Name })
            .Where(x => ids.Contains(x.MovieId))
            .OrderBy(x => x.Name)
            .ToListAsync(ct);
        var genreNamesByMovie = genreRows
            .GroupBy(x => x.MovieId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.Name).ToList());

        var candidates = rows
            .Select(r => new MovieCandidate(r.Id, r.Title, r.OriginalTitle, r.DurationMinutes, r.AgeRating,
                r.PosterUrl, r.Synopsis, genreNamesByMovie.GetValueOrDefault(r.Id, [])))
            .ToList();
        return (candidates, capped);
    }

    private async Task<float[]?> TryEmbedQueryAsync(string text, List<string> warnings, CancellationToken ct)
    {
        var options = gemini.Value;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, options.EmbeddingTimeoutSeconds)));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            var embeddings = await clients.EmbeddingGenerator!.GenerateAsync([text],
                new EmbeddingGenerationOptions { Dimensions = options.EmbeddingDimensions }, linked.Token);
            var vector = embeddings.Single().Vector;
            if (vector.Length != options.EmbeddingDimensions)
                return EmbeddingFailed(warnings);
            return vector.ToArray();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return EmbeddingFailed(warnings);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return EmbeddingFailed(warnings);
        }

        float[]? EmbeddingFailed(List<string> sink)
        {
            sink.Add("Không tạo được vector truy vấn, đã dùng tìm kiếm từ khóa.");
            return null;
        }
    }

    /// <summary>Ranking over indexed documents matching the active embedding model and dimensions.</summary>
    private async Task<IReadOnlyList<(Guid Id, double Score)>?> BuildSemanticRankingAsync(
        float[] queryVector, List<MovieCandidate> candidates, CancellationToken ct)
    {
        var options = gemini.Value;
        var ids = candidates.Select(c => c.Id).ToList();
        var documents = await db.MovieSearchDocuments.AsNoTracking()
            .Where(d => ids.Contains(d.MovieId) && d.Status == MovieSearchDocumentStatus.Indexed &&
                d.EmbeddingModelId == options.EmbeddingModel && d.EmbeddingDimensions == options.EmbeddingDimensions)
            .Select(d => new { d.MovieId, d.Vector })
            .ToListAsync(ct);
        if (documents.Count == 0)
            return null;
        return documents
            .Select(d => (Id: d.MovieId, Score: DiscoveryVectorMath.CosineSimilarity(queryVector, d.Vector)))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Id)
            .ToList();
    }

    private static List<(Guid Id, double Score)> RankByKeyword(List<MovieCandidate> candidates, string keywordQuery) =>
        candidates
            .Select(c => (Id: c.Id, Score: DiscoveryKeywordScorer.Score(keywordQuery, c.Title, c.OriginalTitle, c.GenreNames, c.Synopsis)))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Id)
            .ToList();

    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    private sealed record MovieCandidate(
        Guid Id,
        string Title,
        string? OriginalTitle,
        int DurationMinutes,
        string? AgeRating,
        string? PosterUrl,
        string? Synopsis,
        IReadOnlyList<string> GenreNames);
}
