using LumiCine.Domain.Common;

namespace LumiCine.Domain.Discovery;

/// <summary>
/// A private movie-discovery chat owned by one account. Conditions live in
/// <see cref="FiltersJson"/>; every accepted turn appends messages and bumps the version so
/// concurrent writers with a stale <c>expectedVersion</c> lose.
/// </summary>
public sealed class DiscoveryConversation
{
    private DiscoveryConversation() { }

    public DiscoveryConversation(Guid id, Guid userId, string filtersJson, DateTime createdAtUtc)
    {
        Id = EntityGuard.Id(id, nameof(id));
        UserId = EntityGuard.Id(userId, nameof(userId));
        FiltersJson = EntityGuard.RequiredText(filtersJson, 4000, nameof(filtersJson));
        CreatedAtUtc = EntityGuard.Utc(createdAtUtc, nameof(createdAtUtc));
        LastActiveAtUtc = createdAtUtc;
        Version = 1;
        TurnCount = 0;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public int Version { get; private set; }
    public int TurnCount { get; private set; }
    public string FiltersJson { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime LastActiveAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Opens a new turn: bumps version and turn count up-front so a second writer holding the
    /// stale version loses before spending any AI budget. Filter state lands on completion.
    /// </summary>
    public void BeginTurn(DateTime nowUtc)
    {
        nowUtc = EntityGuard.Utc(nowUtc, nameof(nowUtc));
        if (nowUtc < LastActiveAtUtc)
            throw new InvalidOperationException("Conversation activity cannot move backwards in time.");

        TurnCount++;
        Version++;
        LastActiveAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Commits the turn's resulting filter snapshot without touching version or turn count.</summary>
    public void CompleteTurn(string filtersJson, DateTime nowUtc)
    {
        filtersJson = EntityGuard.RequiredText(filtersJson, 4000, nameof(filtersJson));
        nowUtc = EntityGuard.Utc(nowUtc, nameof(nowUtc));
        if (nowUtc < LastActiveAtUtc)
            throw new InvalidOperationException("Conversation activity cannot move backwards in time.");

        FiltersJson = filtersJson;
        LastActiveAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public bool IsExpired(DateTime nowUtc, TimeSpan inactivityWindow) =>
        LastActiveAtUtc.Add(inactivityWindow) <= EntityGuard.Utc(nowUtc, nameof(nowUtc));

    public DateTime ExpiresAtUtc(TimeSpan inactivityWindow) => LastActiveAtUtc.Add(inactivityWindow);
}
