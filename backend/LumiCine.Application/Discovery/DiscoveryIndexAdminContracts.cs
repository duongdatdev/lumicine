namespace LumiCine.Application.Discovery;

/// <summary>Admin snapshot of the embedding index queue across the catalog.</summary>
public sealed record DiscoveryIndexStatus(int IndexedCount, int PendingCount, int FailedCount, int TotalMovies, DateTime GeneratedAtUtc);

/// <summary>Outcome of a bulk rebuild of the movie search documents.</summary>
public sealed record DiscoveryRebuildResult(int QueuedCount, DateTime QueuedAtUtc);

/// <summary>Admin view over the embedding index queue.</summary>
public interface IDiscoveryIndexAdminService
{
    Task<DiscoveryIndexStatus> GetStatusAsync(CancellationToken cancellationToken);

    Task<DiscoveryRebuildResult> RebuildAsync(CancellationToken cancellationToken);
}
