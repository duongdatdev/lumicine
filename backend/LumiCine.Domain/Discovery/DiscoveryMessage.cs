using LumiCine.Domain.Common;

namespace LumiCine.Domain.Discovery;

/// <summary>
/// One message inside a discovery conversation. A turn is one <see cref="DiscoveryMessageRole.User"/>
/// row plus one assistant row sharing the same <see cref="TurnNumber"/>; assistant rows keep the
/// serialized response so duplicate requests can be replayed without recomputing.
/// </summary>
public sealed class DiscoveryMessage
{
    public const int UserContentMaxLength = 1000;
    public const int AssistantContentMaxLength = 4000;
    public const int ResponseJsonMaxLength = 16000;

    private DiscoveryMessage() { }

    public DiscoveryMessage(Guid id, Guid conversationId, int turnNumber, DiscoveryMessageRole role,
        string content, DateTime createdAtUtc, Guid? requestId = null, string? responseJson = null)
    {
        Id = EntityGuard.Id(id, nameof(id));
        ConversationId = EntityGuard.Id(conversationId, nameof(conversationId));
        if (turnNumber < 1) throw new ArgumentOutOfRangeException(nameof(turnNumber), "Must be positive.");
        TurnNumber = turnNumber;
        Role = EntityGuard.Defined(role, nameof(role));
        Content = EntityGuard.RequiredText(content, ContentMaxLength(role), nameof(content));
        CreatedAtUtc = EntityGuard.Utc(createdAtUtc, nameof(createdAtUtc));
        if (role == DiscoveryMessageRole.User)
        {
            RequestId = EntityGuard.Id(requestId ?? Guid.Empty, nameof(requestId));
            if (responseJson is not null)
                throw new ArgumentException("User messages never carry a stored response.", nameof(responseJson));
        }
        else
        {
            if (requestId is not null)
                throw new ArgumentException("Assistant messages never carry a request id.", nameof(requestId));
            ResponseJson = EntityGuard.OptionalText(responseJson, ResponseJsonMaxLength, nameof(responseJson));
        }
    }

    private static int ContentMaxLength(DiscoveryMessageRole role) =>
        role == DiscoveryMessageRole.User ? UserContentMaxLength : AssistantContentMaxLength;

    public Guid Id { get; private set; }
    public Guid ConversationId { get; private set; }
    public int TurnNumber { get; private set; }
    public DiscoveryMessageRole Role { get; private set; }
    public string Content { get; private set; }
    public Guid? RequestId { get; private set; }
    public string? ResponseJson { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
}
