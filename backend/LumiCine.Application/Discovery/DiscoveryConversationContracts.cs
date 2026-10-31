using System.Text.Json.Serialization;
using LumiCine.Application.Common;

namespace LumiCine.Application.Discovery;

/// <summary>Client payload for one discovery chat turn.</summary>
public sealed record SendDiscoveryMessageRequest(
    [property: JsonRequired] Guid RequestId,
    [property: JsonRequired] int ExpectedVersion,
    [property: JsonRequired] string Message);

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
    /// <summary>
    /// Runs one AI-assisted discovery turn; replays the stored response for repeated
    /// (requestId, payload) pairs and degrades to keyword search plus template answers
    /// whenever an AI step fails.
    /// </summary>
    Task<DiscoveryChatResponse> SendMessageAsync(Guid userId, Guid conversationId, SendDiscoveryMessageRequest request, CancellationToken cancellationToken);
}
