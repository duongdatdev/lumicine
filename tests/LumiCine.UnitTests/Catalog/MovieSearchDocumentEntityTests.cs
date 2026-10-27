using LumiCine.Domain.Catalog;

namespace LumiCine.UnitTests.Catalog;

public sealed class MovieSearchDocumentEntityTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    [Fact]
    public void New_document_is_pending_and_due_immediately()
    {
        var document = NewDocument();

        Assert.Equal(MovieSearchDocumentStatus.Pending, document.Status);
        Assert.Equal(0, document.AttemptCount);
        Assert.Equal(Now, document.NextAttemptAtUtc);
        Assert.Null(document.LeaseUntilUtc);
        Assert.Null(document.Vector);
        Assert.Null(document.EmbeddingModelId);
        Assert.Null(document.IndexedAtUtc);
    }

    [Fact]
    public void New_document_requires_ids_text_and_hex_hash()
    {
        var hash = new string('a', 64);
        Assert.Throws<ArgumentException>(() => new MovieSearchDocument(Guid.Empty, Guid.NewGuid(), "text", hash, Now));
        Assert.Throws<ArgumentException>(() => new MovieSearchDocument(Guid.NewGuid(), Guid.Empty, "text", hash, Now));
        Assert.Throws<ArgumentException>(() => new MovieSearchDocument(Guid.NewGuid(), Guid.NewGuid(), "text", hash, DateTime.Now));
        Assert.Throws<ArgumentException>(() => new MovieSearchDocument(Guid.NewGuid(), Guid.NewGuid(), "text", new string('a', 65), Now));
        Assert.Throws<ArgumentException>(() => new MovieSearchDocument(Guid.NewGuid(), Guid.NewGuid(), "text", new string('g', 64), Now));
        Assert.Throws<ArgumentException>(() => new MovieSearchDocument(Guid.NewGuid(), Guid.NewGuid(), "text", string.Empty, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MovieSearchDocument(Guid.NewGuid(), Guid.NewGuid(), new string('t', 4001), hash, Now));
    }

    [Fact]
    public void Lease_blocks_a_second_worker_until_it_expires()
    {
        var document = NewDocument();

        Assert.True(document.TryAcquireLease(Now, TimeSpan.FromMinutes(5)));
        Assert.False(document.TryAcquireLease(Now.AddMinutes(4), TimeSpan.FromMinutes(5)));
        Assert.True(document.TryAcquireLease(Now.AddMinutes(5), TimeSpan.FromMinutes(5)));
        Assert.Equal(Now.AddMinutes(10), document.LeaseUntilUtc);
    }

    [Fact]
    public void Release_lease_clears_the_lease_but_keeps_status()
    {
        var document = NewDocument();
        document.TryAcquireLease(Now, TimeSpan.FromMinutes(5));

        document.ReleaseLease(Now.AddMinutes(1));

        Assert.Null(document.LeaseUntilUtc);
        Assert.Equal(MovieSearchDocumentStatus.Pending, document.Status);
        Assert.Equal(Now.AddMinutes(1), document.UpdatedAtUtc);
    }

    [Fact]
    public void Indexing_requires_pending_status_and_clears_queue_state()
    {
        var document = NewDocument();
        document.TryAcquireLease(Now, TimeSpan.FromMinutes(5));
        var vector = new float[] { 0.5f, 1.5f };

        document.MarkIndexed("gemini-embedding-2", 768, vector, Now);

        Assert.Equal(MovieSearchDocumentStatus.Indexed, document.Status);
        Assert.Equal("gemini-embedding-2", document.EmbeddingModelId);
        Assert.Equal(768, document.EmbeddingDimensions);
        Assert.Equal(vector, document.Vector);
        Assert.Equal(Now, document.IndexedAtUtc);
        Assert.Null(document.LeaseUntilUtc);
        Assert.Null(document.NextAttemptAtUtc);
        Assert.Null(document.LastError);
        Assert.False(document.TryAcquireLease(Now, TimeSpan.FromMinutes(5)));
        Assert.Throws<InvalidOperationException>(() => document.MarkIndexed("gemini-embedding-2", 768, vector, Now));
    }

    [Fact]
    public void Indexing_rejects_missing_model_or_utc_time()
    {
        var document = NewDocument();
        Assert.Throws<ArgumentException>(() => document.MarkIndexed("  ", 768, [1f], Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.MarkIndexed(new string('m', 101), 768, [1f], Now));
        Assert.Throws<ArgumentException>(() => document.MarkIndexed("gemini-embedding-2", 768, [1f], DateTime.Now));
    }

    [Fact]
    public void Retries_back_off_one_five_fifteen_and_sixty_minutes_then_fail()
    {
        var document = NewDocument();

        document.ScheduleRetry(Now, "first");
        Assert.Equal(1, document.AttemptCount);
        Assert.Equal(Now.AddMinutes(1), document.NextAttemptAtUtc);
        Assert.Equal(MovieSearchDocumentStatus.Pending, document.Status);

        document.ScheduleRetry(Now.AddMinutes(1), "second");
        Assert.Equal(Now.AddMinutes(6), document.NextAttemptAtUtc);

        document.ScheduleRetry(Now.AddMinutes(6), "third");
        Assert.Equal(Now.AddMinutes(21), document.NextAttemptAtUtc);

        document.ScheduleRetry(Now.AddMinutes(21), "fourth");
        Assert.Equal(Now.AddMinutes(81), document.NextAttemptAtUtc);
        Assert.Equal(MovieSearchDocumentStatus.Pending, document.Status);

        document.ScheduleRetry(Now.AddMinutes(81), "fifth");
        Assert.Equal(5, document.AttemptCount);
        Assert.Equal(MovieSearchDocumentStatus.Failed, document.Status);
        Assert.Null(document.NextAttemptAtUtc);
        Assert.Null(document.LeaseUntilUtc);
        Assert.Equal("fifth", document.LastError);
    }

    [Fact]
    public void Retry_needs_pending_status_and_caps_error_length()
    {
        var document = NewDocument();
        Assert.Throws<ArgumentOutOfRangeException>(() => document.ScheduleRetry(Now, new string('e', 501)));

        document.MarkIndexed("gemini-embedding-2", 768, [1f], Now);
        Assert.Throws<InvalidOperationException>(() => document.ScheduleRetry(Now, "late failure"));
    }

    [Fact]
    public void Mark_pending_resets_a_failed_document_for_a_fresh_cycle()
    {
        var document = NewDocument();
        document.ScheduleRetry(Now, "embedding failed");
        document.ScheduleRetry(Now.AddMinutes(1), "embedding failed");
        document.ScheduleRetry(Now.AddMinutes(6), "embedding failed");
        document.ScheduleRetry(Now.AddMinutes(21), "embedding failed");
        document.ScheduleRetry(Now.AddMinutes(81), "embedding failed");
        Assert.Equal(MovieSearchDocumentStatus.Failed, document.Status);

        document.MarkPending(Now.AddHours(2));

        Assert.Equal(MovieSearchDocumentStatus.Pending, document.Status);
        Assert.Equal(0, document.AttemptCount);
        Assert.Equal(Now.AddHours(2), document.NextAttemptAtUtc);
        Assert.Null(document.LastError);
        Assert.Null(document.LeaseUntilUtc);
        Assert.Null(document.Vector);
        Assert.Null(document.EmbeddingModelId);
        Assert.Null(document.EmbeddingDimensions);
        Assert.Null(document.IndexedAtUtc);
        Assert.Equal("text", document.DocumentText);
        Assert.Equal(64, document.SourceHash.Length);
    }

    [Fact]
    public void Update_source_ignores_unchanged_content_and_resets_changed_content()
    {
        var document = NewDocument();
        document.TryAcquireLease(Now, TimeSpan.FromMinutes(5));
        document.ScheduleRetry(Now, "embedding failed");

        Assert.False(document.UpdateSource("text", document.SourceHash, Now.AddMinutes(1)));

        var newHash = new string('f', 64);
        Assert.True(document.UpdateSource("text v2", newHash, Now.AddMinutes(1)));

        Assert.Equal("text v2", document.DocumentText);
        Assert.Equal(newHash, document.SourceHash);
        Assert.Equal(MovieSearchDocumentStatus.Pending, document.Status);
        Assert.Equal(0, document.AttemptCount);
        Assert.Equal(Now.AddMinutes(1), document.NextAttemptAtUtc);
        Assert.Null(document.LeaseUntilUtc);
        Assert.Null(document.LastError);
        Assert.Null(document.Vector);
        Assert.Null(document.IndexedAtUtc);
    }

    [Fact]
    public void Update_source_rejects_invalid_text_or_hash()
    {
        var document = NewDocument();
        Assert.Throws<ArgumentOutOfRangeException>(() => document.UpdateSource(new string('t', 4001), document.SourceHash, Now));
        Assert.Throws<ArgumentException>(() => document.UpdateSource("text v2", new string('a', 10), Now));
        Assert.Throws<ArgumentException>(() => document.UpdateSource("text v2", document.SourceHash, DateTime.Now));
    }

    private static MovieSearchDocument NewDocument() => new(Guid.NewGuid(), Guid.NewGuid(), "text", new string('a', 64), Now);
}
