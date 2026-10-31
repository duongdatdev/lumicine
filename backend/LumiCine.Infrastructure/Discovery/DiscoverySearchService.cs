using LumiCine.Application.Discovery;
using LumiCine.Domain.Bookings;
using LumiCine.Domain.Catalog;
using LumiCine.Domain.Cinemas;
using LumiCine.Domain.Scheduling;
using LumiCine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace LumiCine.Infrastructure.Discovery;

/// <summary>
/// Retrieval stage of discovery: loads filtered movie candidates, ranks them by cosine
/// similarity over movie embeddings fused with keyword scores, then keeps only movies whose
/// showtimes still have live availability inside the requested window.
/// </summary>
public sealed class DiscoverySearchService(
    LumiCineDbContext db,
    TimeProvider clock,
    GeminiAiClients clients,
    IOptions<GeminiOptions> gemini,
    IOptions<DiscoveryConversationOptions> conversation) : IDiscoverySearchService
{
    private const int CandidateCap = 5000;

    // Upper bound for the per-movie showtime scan; the zone time-of-day filter runs in memory,
    // so the earliest qualifying showtimes come from the first N upcoming rows.
    private const int ShowtimeScanCap = 200;

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
        // When the parser produced no topic text (degraded turn), the raw message still ranks
        // keyword matches; concrete-only queries keep their schedule ordering untouched.
        var rankText = hasTextQuery ? keywordQuery : (input.MessageText ?? string.Empty);

        float[]? queryVector = null;
        if (hasTextQuery && !capped && candidates.Count > 0 && clients.IsConfigured)
        {
            queryVector = await TryEmbedQueryAsync(keywordQuery, warnings, cancellationToken);
            isDegraded |= queryVector is null;
        }

        var semanticRanking = queryVector is null
            ? null
            : await BuildSemanticRankingAsync(queryVector, candidates, cancellationToken);
        var keywordRanking = RankByKeyword(candidates, rankText);

        string mode;
        IReadOnlyList<(Guid Id, double Score)> ranked;
        if (!hasTextQuery)
        {
            // Pure schedule search: relevance comes from showtimes, not text.
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
        var selected = await SelectByAvailabilityAsync(ranked, byId, filters, windowStart, windowEnd, now, hasTextQuery, cancellationToken);
        var results = new List<DiscoveryMovieResult>();
        foreach (var movie in selected)
        {
            var candidate = byId[movie.MovieId];
            var showtimes = movie.Showtimes
                .Select(st => new DiscoveryShowtimeResult(st.ShowtimeId, movie.MovieId, st.CinemaId, st.CinemaName,
                    st.RoomId, st.RoomName, st.StartAtUtc, st.CheapestPrice ?? st.BasePrice, st.AvailableSeats))
                .ToList();
            results.Add(new DiscoveryMovieResult(candidate.Id, candidate.Title, candidate.OriginalTitle,
                candidate.DurationMinutes, candidate.AgeRating, candidate.PosterUrl, candidate.GenreNames, showtimes, movie.Score));
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

        var scheduled = ScheduledShowtimes(filters, windowStart, windowEnd, now);
        var movies = db.Movies.AsNoTracking().Where(m => m.IsActive && scheduled.Any(s => s.MovieId == m.Id));
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

    /// <summary>
    /// Scheduled showtimes in the search window whose room and cinema are active and satisfy the
    /// cinema identity filters. Entity-rooted so both the candidate gate and the availability
    /// walk compose over the same definition.
    /// </summary>
    private IQueryable<Showtime> ScheduledShowtimes(DiscoveryFilters filters, DateTime windowStart, DateTime windowEnd, DateTime now)
    {
        var cityLower = string.IsNullOrWhiteSpace(filters.City) ? null : filters.City.Trim().ToLower();
        return db.Showtimes.AsNoTracking().Where(s =>
            s.Status == ShowtimeStatus.Scheduled &&
            s.StartAtUtc >= windowStart && s.StartAtUtc >= now && s.StartAtUtc < windowEnd &&
            db.Rooms.Any(r => r.Id == s.RoomId && r.IsActive &&
                db.Cinemas.Any(c => c.Id == r.CinemaId && c.IsActive &&
                    (filters.CinemaId == null || c.Id == filters.CinemaId) &&
                    (cityLower == null || c.City.ToLower() == cityLower))));
    }

    /// <summary>Named-slot projection for showtimes; must stay the final query operation.</summary>
    private IQueryable<ShowtimeSlot> ProjectSlots(IQueryable<Showtime> showtimes) =>
        from s in showtimes
        join r in db.Rooms.AsNoTracking() on s.RoomId equals r.Id
        join c in db.Cinemas.AsNoTracking() on r.CinemaId equals c.Id
        select new ShowtimeSlot(s.Id, s.MovieId, r.Id, r.Name, c.Id, c.Name, s.StartAtUtc, s.BasePrice);

    /// <summary>
    /// Walks the ranking in order, keeps the earliest qualifying showtimes per movie and stops
    /// after the movie limit; the final set is re-verified against fresh data without backfill.
    /// </summary>
    private async Task<List<SelectedMovie>> SelectByAvailabilityAsync(
        IReadOnlyList<(Guid Id, double Score)> ranked,
        Dictionary<Guid, MovieCandidate> byId,
        DiscoveryFilters filters,
        DateTime windowStart,
        DateTime windowEnd,
        DateTime now,
        bool hasTextQuery,
        CancellationToken ct)
    {
        var limit = conversation.Value.ResultMovieLimit;
        var selected = new List<SelectedMovie>();
        foreach (var (movieId, score) in ranked)
        {
            if (selected.Count >= limit)
                break;
            if (!byId.ContainsKey(movieId))
                continue;
            var showtimes = await LoadQualifyingShowtimesAsync(movieId, filters, windowStart, windowEnd, now, ct);
            if (showtimes.Count > 0)
                selected.Add(new SelectedMovie(movieId, score, showtimes));
        }

        var verified = await ReverifyAsync(selected, filters, windowStart, windowEnd, ct);
        if (!hasTextQuery)
        {
            // No relevance signal: the earliest qualifying showtime is the ranking.
            verified = verified
                .OrderBy(s => s.Showtimes.Min(st => st.StartAtUtc))
                .ThenBy(s => s.MovieId)
                .ToList();
        }
        return verified;
    }

    private async Task<List<QualifyingShowtime>> LoadQualifyingShowtimesAsync(
        Guid movieId, DiscoveryFilters filters, DateTime windowStart, DateTime windowEnd, DateTime now, CancellationToken ct)
    {
        var rows = await ProjectSlots(ScheduledShowtimes(filters, windowStart, windowEnd, now)
                .Where(s => s.MovieId == movieId)
                .OrderBy(s => s.StartAtUtc).ThenBy(s => s.Id)
                .Take(ShowtimeScanCap))
            .ToListAsync(ct);
        // Zone wall-clock minutes are not translatable, so the time window filters in memory.
        var inTimeWindow = filters.TimeWindowStartMinutes is int startMinutes && filters.TimeWindowEndMinutes is int endMinutes
            ? rows.Where(x => InTimeWindow(DiscoveryTime.MinutesOfDayInZone(x.StartAtUtc), startMinutes, endMinutes)).ToList()
            : rows;
        var seatStats = await LoadSeatStatsAsync(inTimeWindow.Select(x => x.ShowtimeId).ToList(), now, ct);

        var qualifying = new List<QualifyingShowtime>();
        foreach (var row in inTimeWindow)
        {
            var (availableSeats, cheapestPrice) = seatStats.GetValueOrDefault(row.ShowtimeId);
            if (filters.MinSeats is int minSeats && availableSeats < minSeats)
                continue;
            if (filters.MaxPrice is decimal maxPrice && (cheapestPrice is not decimal price || price > maxPrice))
                continue;
            qualifying.Add(new QualifyingShowtime(row.ShowtimeId, row.RoomId, row.RoomName, row.CinemaId, row.CinemaName,
                row.StartAtUtc, row.BasePrice, availableSeats, cheapestPrice));
            if (qualifying.Count >= conversation.Value.ShowtimesPerMovieLimit)
                break;
        }
        return qualifying;
    }

    /// <summary>Effective seat counts per showtime, mirroring ShowtimeService.GetSeatsAsync.</summary>
    private async Task<Dictionary<Guid, (int AvailableSeats, decimal? CheapestPrice)>> LoadSeatStatsAsync(
        List<Guid> showtimeIds, DateTime now, CancellationToken ct)
    {
        if (showtimeIds.Count == 0)
            return [];
        var rows = await (from showtimeSeat in db.ShowtimeSeats.AsNoTracking()
                          join seat in db.Seats.AsNoTracking() on new { showtimeSeat.SeatId, showtimeSeat.RoomId } equals new { SeatId = seat.Id, seat.RoomId }
                          join booking in db.Bookings.AsNoTracking() on showtimeSeat.CurrentBookingId equals booking.Id into bookings
                          from booking in bookings.DefaultIfEmpty()
                          where showtimeIds.Contains(showtimeSeat.ShowtimeId)
                          select new
                          {
                              showtimeSeat.ShowtimeId,
                              showtimeSeat.Status,
                              showtimeSeat.Price,
                              SeatIsActive = seat.IsActive,
                              BookingStatus = (BookingStatus?)booking.Status,
                              booking.HoldExpiresAtUtc
                          })
            .ToListAsync(ct);

        var stats = new Dictionary<Guid, (int, decimal?)>();
        foreach (var group in rows.Where(x => x.SeatIsActive).GroupBy(x => x.ShowtimeId))
        {
            var available = group
                .Where(x => IsEffectivelyAvailable(x.Status, x.BookingStatus, x.HoldExpiresAtUtc, now))
                .ToList();
            stats[group.Key] = (available.Count, available.Count == 0 ? null : available.Min(x => x.Price));
        }
        return stats;
    }

    private static bool IsEffectivelyAvailable(ShowtimeSeatStatus status, BookingStatus? bookingStatus, DateTime? holdExpiresAtUtc, DateTime now) =>
        status == ShowtimeSeatStatus.Available ||
        (status == ShowtimeSeatStatus.Held && bookingStatus == BookingStatus.Expired) ||
        (status == ShowtimeSeatStatus.Held && bookingStatus == BookingStatus.Pending && holdExpiresAtUtc is DateTime expiry && expiry <= now);

    private static bool InTimeWindow(int minutes, int startMinutes, int endMinutes) =>
        startMinutes <= endMinutes
            ? minutes >= startMinutes && minutes <= endMinutes
            : minutes >= startMinutes || minutes <= endMinutes;

    /// <summary>
    /// Re-queries the selected movies, showtimes and seats fresh and drops anything that no
    /// longer qualifies; dropped movies are not backfilled from lower-ranked candidates.
    /// </summary>
    private async Task<List<SelectedMovie>> ReverifyAsync(
        List<SelectedMovie> selected, DiscoveryFilters filters, DateTime windowStart, DateTime windowEnd, CancellationToken ct)
    {
        if (selected.Count == 0)
            return [];
        var now = UtcNow;
        var movieIds = selected.Select(s => s.MovieId).ToList();
        var activeMovieIds = (await db.Movies.AsNoTracking()
            .Where(m => movieIds.Contains(m.Id) && m.IsActive)
            .Select(m => m.Id)
            .ToListAsync(ct)).ToHashSet();
        var showtimeIds = selected.SelectMany(s => s.Showtimes).Select(st => st.ShowtimeId).ToList();
        var freshRows = await ProjectSlots(ScheduledShowtimes(filters, windowStart, windowEnd, now)
                .Where(s => showtimeIds.Contains(s.Id)))
            .ToListAsync(ct);
        var freshById = freshRows.ToDictionary(x => x.ShowtimeId);
        var seatStats = await LoadSeatStatsAsync(showtimeIds, now, ct);

        var verified = new List<SelectedMovie>();
        foreach (var movie in selected)
        {
            if (!activeMovieIds.Contains(movie.MovieId))
                continue;
            var kept = new List<QualifyingShowtime>();
            foreach (var showtime in movie.Showtimes)
            {
                if (!freshById.TryGetValue(showtime.ShowtimeId, out var fresh))
                    continue; // no longer scheduled, future, active or inside the cinema filters
                if (filters.TimeWindowStartMinutes is int startMinutes && filters.TimeWindowEndMinutes is int endMinutes &&
                    !InTimeWindow(DiscoveryTime.MinutesOfDayInZone(fresh.StartAtUtc), startMinutes, endMinutes))
                    continue;
                var (availableSeats, cheapestPrice) = seatStats.GetValueOrDefault(fresh.ShowtimeId);
                if (filters.MinSeats is int minSeats && availableSeats < minSeats)
                    continue;
                if (filters.MaxPrice is decimal maxPrice && (cheapestPrice is not decimal price || price > maxPrice))
                    continue;
                kept.Add(new QualifyingShowtime(fresh.ShowtimeId, fresh.RoomId, fresh.RoomName, fresh.CinemaId, fresh.CinemaName,
                    fresh.StartAtUtc, fresh.BasePrice, availableSeats, cheapestPrice));
            }
            if (kept.Count > 0)
                verified.Add(new SelectedMovie(movie.MovieId, movie.Score, kept));
        }
        return verified;
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

    private sealed record ShowtimeSlot(
        Guid ShowtimeId,
        Guid MovieId,
        Guid RoomId,
        string RoomName,
        Guid CinemaId,
        string CinemaName,
        DateTime StartAtUtc,
        decimal BasePrice);

    private sealed record QualifyingShowtime(
        Guid ShowtimeId,
        Guid RoomId,
        string RoomName,
        Guid CinemaId,
        string CinemaName,
        DateTime StartAtUtc,
        decimal BasePrice,
        int AvailableSeats,
        decimal? CheapestPrice);

    private sealed record SelectedMovie(Guid MovieId, double Score, IReadOnlyList<QualifyingShowtime> Showtimes);
}
