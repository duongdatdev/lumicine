using LumiCine.Application.Discovery;
using LumiCine.Domain.Catalog;
using LumiCine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LumiCine.Infrastructure.Discovery;

/// <summary>
/// Admin view over the embedding queue plus a bulk rebuild that re-derives every active movie's
/// document from the current catalog state; it complements the per-movie upserts of the catalog
/// service for documents drifted out of band or stuck in a failed state.
/// </summary>
#pragma warning disable CS9113 // Parameter 'gemini' is unread; reserved for embedding-model metadata once the admin view reports it.
public sealed class DiscoveryIndexAdminService(
    LumiCineDbContext db,
    TimeProvider clock,
    IMovieSearchDocumentBuilder builder,
    IOptions<GeminiOptions> gemini) : IDiscoveryIndexAdminService
{
    public async Task<DiscoveryIndexStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var groups = await db.MovieSearchDocuments.AsNoTracking()
            .GroupBy(document => document.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var indexed = 0;
        var pending = 0;
        var failed = 0;
        foreach (var group in groups)
        {
            switch (group.Status)
            {
                case MovieSearchDocumentStatus.Indexed: indexed = group.Count; break;
                case MovieSearchDocumentStatus.Pending: pending = group.Count; break;
                case MovieSearchDocumentStatus.Failed: failed = group.Count; break;
            }
        }
        var totalMovies = await db.Movies.CountAsync(cancellationToken);
        return new(indexed, pending, failed, totalMovies, clock.GetUtcNow().UtcDateTime);
    }

    // Stages the rebuild from each active movie's current catalog state and saves once, so a
    // partial catalog read can never commit a half-queued rebuild.
    public async Task<DiscoveryRebuildResult> RebuildAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var movies = await db.Movies.AsNoTracking()
            .Where(movie => movie.IsActive)
            .OrderBy(movie => movie.Id)
            .Select(movie => new { movie.Id, movie.Title, movie.OriginalTitle, movie.Synopsis })
            .ToListAsync(cancellationToken);
        var queuedCount = 0;
        foreach (var movie in movies)
        {
            var genreNames = await db.MovieGenres.Where(mg => mg.MovieId == movie.Id)
                .Join(db.Genres, mg => mg.GenreId, g => g.Id, (mg, g) => g)
                .OrderBy(g => g.Name).ThenBy(g => g.Id)
                .Select(g => g.Name)
                .ToListAsync(cancellationToken);
            var text = builder.BuildDocumentText(movie.Title, movie.OriginalTitle, movie.Synopsis, genreNames);
            var hash = builder.ComputeSourceHash(text);
            var document = await db.MovieSearchDocuments.SingleOrDefaultAsync(d => d.MovieId == movie.Id, cancellationToken);
            if (document is null)
            {
                db.MovieSearchDocuments.Add(new MovieSearchDocument(Guid.NewGuid(), movie.Id, text, hash, now));
                queuedCount++;
            }
            else if (document.SourceHash != hash)
            {
                document.UpdateSource(text, hash, now);
                queuedCount++;
            }
            else if (document.Status == MovieSearchDocumentStatus.Failed)
            {
                document.MarkPending(now);
                queuedCount++;
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        return new(queuedCount, now);
    }
}
#pragma warning restore CS9113
