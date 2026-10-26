using LumiCine.Application.Common;

namespace LumiCine.Application.Discovery;

public sealed record DiscoveryConversationSummary(
    Guid Id,
    int Version,
    int TurnCount,
    bool IsExpired,
    DateTime CreatedAtUtc,
    DateTime LastActiveAtUtc,
    DateTime ExpiresAtUtc);

public sealed record DiscoveryMessageView(
    int TurnNumber,
    string Role,
    string Content,
    DateTime CreatedAtUtc);

/// <summary>Full conversation state: current filters plus the complete message history.</summary>
public sealed record DiscoveryConversationDetails(
    Guid Id,
    int Version,
    int TurnCount,
    bool IsExpired,
    DiscoveryFiltersView Filters,
    IReadOnlyList<DiscoveryMessageView> Messages,
    DateTime CreatedAtUtc,
    DateTime LastActiveAtUtc,
    DateTime ExpiresAtUtc);

/// <summary>Conversations belong to their owner; foreign ids look like missing rows.</summary>
public interface IDiscoveryConversationService
{
    Task<DiscoveryConversationDetails> CreateAsync(Guid userId, CancellationToken cancellationToken);
    Task<PagedResult<DiscoveryConversationSummary>> ListAsync(Guid userId, PageQuery page, CancellationToken cancellationToken);
    Task<DiscoveryConversationDetails?> GetAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken);
}
