namespace LumiCine.Application.Discovery;

/// <summary>One leased queue row handed to the embedding worker.</summary>
public sealed record MovieEmbeddingWork(Guid DocumentId, Guid MovieId, string DocumentText, string SourceHash);

/// <summary>Composes the Vietnamese catalog text embedded per movie and hashes it for change detection.</summary>
public interface IMovieSearchDocumentBuilder
{
    string BuildDocumentText(string title, string? originalTitle, string? synopsis, IReadOnlyList<string> genreNames);

    string ComputeSourceHash(string documentText);
}

/// <summary>Lease-queued access to the movie embedding queue rows.</summary>
public interface IMovieSearchDocumentService
{
    Task<IReadOnlyList<MovieEmbeddingWork>> ClaimDueAsync(int batchSize, CancellationToken cancellationToken);

    Task CompleteAsync(Guid documentId, string embeddingModelId, int dimensions, float[] vector, string expectedSourceHash, CancellationToken cancellationToken);

    Task RetryAsync(Guid documentId, string error, CancellationToken cancellationToken);
}
