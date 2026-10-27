using LumiCine.Application.Discovery;
using LumiCine.Domain.Catalog;
using LumiCine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LumiCine.Infrastructure.Discovery;

public sealed class MovieSearchDocumentService(LumiCineDbContext db, TimeProvider clock) : IMovieSearchDocumentService
{
    public async Task<IReadOnlyList<MovieEmbeddingWork>> ClaimDueAsync(int batchSize, CancellationToken ct)
    {
        var now = UtcNow;
        var candidates = await db.MovieSearchDocuments.AsNoTracking()
            .Where(x => x.Status == MovieSearchDocumentStatus.Pending && x.NextAttemptAtUtc != null && x.NextAttemptAtUtc <= now &&
                (x.LeaseUntilUtc == null || x.LeaseUntilUtc <= now))
            .OrderBy(x => x.NextAttemptAtUtc).ThenBy(x => x.Id)
            .Take(Math.Clamp(batchSize, 1, 100))
            .Select(x => new { x.Id, x.MovieId, x.DocumentText, x.SourceHash })
            .ToListAsync(ct);
        var leaseUntil = now.Add(TimeSpan.FromMinutes(5));
        var claimed = new List<MovieEmbeddingWork>();
        foreach (var candidate in candidates)
        {
            // The conditional update is the atomic lease: only one worker's update affects the row.
            var affected = await db.MovieSearchDocuments
                .Where(x => x.Id == candidate.Id && x.Status == MovieSearchDocumentStatus.Pending &&
                    (x.LeaseUntilUtc == null || x.LeaseUntilUtc <= now))
                .ExecuteUpdateAsync(update => update
                    .SetProperty(x => x.LeaseUntilUtc, leaseUntil)
                    .SetProperty(x => x.UpdatedAtUtc, now), ct);
            if (affected == 1)
                claimed.Add(new MovieEmbeddingWork(candidate.Id, candidate.MovieId, candidate.DocumentText, candidate.SourceHash));
        }
        return claimed;
    }

    public async Task CompleteAsync(Guid documentId, string embeddingModelId, int dimensions, float[] vector, string expectedSourceHash, CancellationToken ct)
    {
        var document = await db.MovieSearchDocuments.SingleOrDefaultAsync(x => x.Id == documentId, ct);
        if (document is null || document.SourceHash != expectedSourceHash)
            return;
        document.MarkIndexed(embeddingModelId, dimensions, vector, UtcNow);
        await db.SaveChangesAsync(ct);
    }

    public async Task RetryAsync(Guid documentId, string error, CancellationToken ct)
    {
        var document = await db.MovieSearchDocuments.SingleOrDefaultAsync(x => x.Id == documentId, ct);
        if (document is null)
            return;
        document.ScheduleRetry(UtcNow, error);
        await db.SaveChangesAsync(ct);
    }

    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;
}
