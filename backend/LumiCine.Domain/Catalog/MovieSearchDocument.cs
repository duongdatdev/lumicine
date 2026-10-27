using LumiCine.Domain.Common;

namespace LumiCine.Domain.Catalog;

/// <summary>
/// Embedding-queue row for one movie: the document text plus a hash of the catalog source that
/// produced it, claimed with a lease by the embedding worker like the ticket email queue.
/// </summary>
public sealed class MovieSearchDocument
{
    public const int MaxAttempts = 5;

    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(60)
    ];

    private MovieSearchDocument() { }

    public MovieSearchDocument(Guid id, Guid movieId, string documentText, string sourceHash, DateTime createdAtUtc)
    {
        Id = EntityGuard.Id(id, nameof(id));
        MovieId = EntityGuard.Id(movieId, nameof(movieId));
        DocumentText = EntityGuard.RequiredText(documentText, 4000, nameof(documentText));
        SourceHash = RequireHash(sourceHash);
        Status = MovieSearchDocumentStatus.Pending;
        CreatedAtUtc = EntityGuard.Utc(createdAtUtc, nameof(createdAtUtc));
        NextAttemptAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid MovieId { get; private set; }
    public string DocumentText { get; private set; } = string.Empty;
    public string SourceHash { get; private set; } = string.Empty;
    public string? EmbeddingModelId { get; private set; }
    public int? EmbeddingDimensions { get; private set; }
    public float[]? Vector { get; private set; }
    public MovieSearchDocumentStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTime? NextAttemptAtUtc { get; private set; }
    public DateTime? LeaseUntilUtc { get; private set; }
    public string? LastError { get; private set; }
    public DateTime? IndexedAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    public bool TryAcquireLease(DateTime nowUtc, TimeSpan leaseDuration)
    {
        nowUtc = EntityGuard.Utc(nowUtc, nameof(nowUtc));
        if (leaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), "Must be greater than zero.");
        if (Status != MovieSearchDocumentStatus.Pending)
            return false;
        if (LeaseUntilUtc is DateTime lease && lease > nowUtc)
            return false;

        LeaseUntilUtc = nowUtc.Add(leaseDuration);
        UpdatedAtUtc = nowUtc;
        return true;
    }

    public void ReleaseLease(DateTime nowUtc)
    {
        LeaseUntilUtc = null;
        UpdatedAtUtc = EntityGuard.Utc(nowUtc, nameof(nowUtc));
    }

    public void MarkIndexed(string modelId, int dimensions, float[] vector, DateTime nowUtc)
    {
        nowUtc = EntityGuard.Utc(nowUtc, nameof(nowUtc));
        if (Status != MovieSearchDocumentStatus.Pending)
            throw new InvalidOperationException("Only pending movie search documents can be indexed.");
        var validatedModelId = EntityGuard.RequiredText(modelId, 100, nameof(modelId));

        Status = MovieSearchDocumentStatus.Indexed;
        EmbeddingModelId = validatedModelId;
        EmbeddingDimensions = dimensions;
        Vector = vector;
        IndexedAtUtc = nowUtc;
        LeaseUntilUtc = null;
        LastError = null;
        NextAttemptAtUtc = null;
        UpdatedAtUtc = nowUtc;
    }

    public void ScheduleRetry(DateTime nowUtc, string error)
    {
        nowUtc = EntityGuard.Utc(nowUtc, nameof(nowUtc));
        if (Status != MovieSearchDocumentStatus.Pending)
            throw new InvalidOperationException("Only pending movie search documents can be retried.");

        AttemptCount++;
        LeaseUntilUtc = null;
        LastError = error is null ? null : EntityGuard.OptionalText(error, 500, nameof(error));
        if (AttemptCount >= MaxAttempts)
        {
            Status = MovieSearchDocumentStatus.Failed;
            NextAttemptAtUtc = null;
        }
        else
        {
            NextAttemptAtUtc = nowUtc.Add(RetryDelays[Math.Min(AttemptCount, RetryDelays.Length) - 1]);
        }
        UpdatedAtUtc = nowUtc;
    }

    public void MarkPending(DateTime nowUtc)
    {
        nowUtc = EntityGuard.Utc(nowUtc, nameof(nowUtc));
        Status = MovieSearchDocumentStatus.Pending;
        AttemptCount = 0;
        NextAttemptAtUtc = nowUtc;
        LeaseUntilUtc = null;
        LastError = null;
        EmbeddingModelId = null;
        EmbeddingDimensions = null;
        Vector = null;
        IndexedAtUtc = null;
        UpdatedAtUtc = nowUtc;
    }

    public bool UpdateSource(string documentText, string sourceHash, DateTime nowUtc)
    {
        if (documentText == DocumentText && sourceHash == SourceHash)
            return false;

        var validatedText = EntityGuard.RequiredText(documentText, 4000, nameof(documentText));
        var validatedHash = RequireHash(sourceHash);
        var utc = EntityGuard.Utc(nowUtc, nameof(nowUtc));
        DocumentText = validatedText;
        SourceHash = validatedHash;
        MarkPending(utc);
        return true;
    }

    private static string RequireHash(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(value));
        return value.Length == 64 && value.All(char.IsAsciiHexDigit)
            ? value
            : throw new ArgumentException("Source hash must be 64 hexadecimal characters.", nameof(value));
    }
}
