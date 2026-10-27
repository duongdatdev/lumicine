namespace LumiCine.Application.Discovery;

/// <summary>Client payload for one chat turn; guards concurrent context edits and replays.</summary>
public sealed record DiscoveryBeginTurnRequest(
    Guid RequestId,
    int ExpectedVersion,
    string Message);

/// <summary>State handed to the orchestration after a turn is safely opened.</summary>
public sealed record DiscoveryTurnContext(
    Guid ConversationId,
    int Version,
    int TurnNumber,
    string FiltersJson,
    IReadOnlyList<DiscoveryIntentTurn> RecentTurns);

public sealed record BeginDiscoveryTurnResult(
    DiscoveryTurnContext? Context,
    DiscoveryChatResponse? ReplayedResponse);

/// <summary>
/// Transactional plumbing for chat turns: opens turns with version checks and replay detection,
/// and commits the assistant result once the response is fully computed.
/// </summary>
public interface IDiscoveryConversationTurnStore
{
    /// <summary>
    /// Opens a turn for a new message, or replays the stored response for a repeated
    /// (requestId, payload) pair. Throws 404/409/410/400 through <see cref="Application.Common.ApiException"/>.
    /// </summary>
    Task<BeginDiscoveryTurnResult> BeginAsync(Guid userId, Guid conversationId, DiscoveryBeginTurnRequest request, CancellationToken cancellationToken);

    /// <summary>Stores the assistant answer plus resulting filters for an already-opened turn.</summary>
    Task CompleteAsync(Guid userId, Guid conversationId, int turnNumber, string filtersJson, DiscoveryChatResponse response, string assistantContent, CancellationToken cancellationToken);
}
