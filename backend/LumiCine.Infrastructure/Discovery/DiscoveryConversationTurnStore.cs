using System.Text.Json;
using LumiCine.Application.Common;
using LumiCine.Application.Discovery;
using LumiCine.Domain.Discovery;
using LumiCine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LumiCine.Infrastructure.Discovery;

public sealed class DiscoveryConversationTurnStore(
    LumiCineDbContext db,
    TimeProvider clock,
    IOptions<DiscoveryConversationOptions> options) : IDiscoveryConversationTurnStore
{
    private TimeSpan InactivityWindow => TimeSpan.FromDays(options.Value.InactivityDays);
    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    public async Task<BeginDiscoveryTurnResult> BeginAsync(Guid userId, Guid conversationId, DiscoveryBeginTurnRequest request, CancellationToken cancellationToken)
    {
        if (request.RequestId == Guid.Empty)
            throw new ApiException(400, "RequestId must be provided.");
        var message = request.Message?.Trim() ?? string.Empty;
        if (message.Length == 0)
            throw new ApiException(400, "Message must not be empty.");
        if (message.Length > options.Value.MessageMaxLength)
            throw new ApiException(400, $"Message must be at most {options.Value.MessageMaxLength} characters.");

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.LockRowAsync("DiscoveryConversations", conversationId, cancellationToken);
        var conversation = await db.DiscoveryConversations.SingleOrDefaultAsync(c => c.Id == conversationId, cancellationToken)
            ?? throw new ApiException(404, "Conversation not found.");
        if (conversation.UserId != userId)
            throw new ApiException(404, "Conversation not found.");
        if (conversation.IsExpired(UtcNow, InactivityWindow))
            throw new ApiException(410, "Conversation has expired after inactivity.");

        var prior = await db.DiscoveryMessages
            .SingleOrDefaultAsync(m => m.ConversationId == conversationId && m.RequestId == request.RequestId, cancellationToken);
        if (prior is not null)
        {
            if (!string.Equals(prior.Content, message, StringComparison.Ordinal))
                throw new ApiException(409, "This requestId was already used with a different message.");
            var storedAnswer = await db.DiscoveryMessages
                .SingleOrDefaultAsync(m => m.ConversationId == conversationId && m.TurnNumber == prior.TurnNumber && m.Role == DiscoveryMessageRole.Assistant, cancellationToken);
            if (storedAnswer?.ResponseJson is string json)
                return new BeginDiscoveryTurnResult(null, JsonSerializer.Deserialize<DiscoveryChatResponse>(json));
            // A previous attempt crashed after opening this turn: reprocess the same turn.
            var retryContext = await BuildContextAsync(conversationId, prior.TurnNumber, cancellationToken);
            return new BeginDiscoveryTurnResult(retryContext, null);
        }

        if (conversation.TurnCount >= options.Value.MaxTurns)
            throw new ApiException(409, "Conversation turn limit has been reached.");
        if (conversation.Version != request.ExpectedVersion)
            throw new ApiException(409, "Conversation was updated concurrently; refresh and retry.");

        var turnNumber = conversation.TurnCount + 1;
        conversation.BeginTurn(UtcNow);
        db.DiscoveryMessages.Add(new DiscoveryMessage(Guid.NewGuid(), conversationId, turnNumber,
            DiscoveryMessageRole.User, message, UtcNow, requestId: request.RequestId));
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        var context = await BuildContextAsync(conversationId, turnNumber, cancellationToken);
        return new BeginDiscoveryTurnResult(context, null);
    }

    public async Task CompleteAsync(Guid userId, Guid conversationId, int turnNumber, string filtersJson, DiscoveryChatResponse response, string assistantContent, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(response);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.LockRowAsync("DiscoveryConversations", conversationId, cancellationToken);
        var conversation = await db.DiscoveryConversations.SingleOrDefaultAsync(c => c.Id == conversationId, cancellationToken)
            ?? throw new ApiException(404, "Conversation not found.");
        if (conversation.UserId != userId)
            throw new ApiException(404, "Conversation not found.");
        if (conversation.TurnCount != turnNumber)
            throw new ApiException(409, "Turn was superseded by a newer conversation update.");

        db.DiscoveryMessages.Add(new DiscoveryMessage(Guid.NewGuid(), conversationId, turnNumber,
            DiscoveryMessageRole.Assistant, assistantContent, UtcNow, responseJson: json));
        conversation.CompleteTurn(filtersJson, UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    private async Task<DiscoveryTurnContext> BuildContextAsync(Guid conversationId, int turnNumber, CancellationToken cancellationToken)
    {
        var conversation = await db.DiscoveryConversations.AsNoTracking()
            .SingleAsync(c => c.Id == conversationId, cancellationToken);
        var recent = await db.DiscoveryMessages.AsNoTracking()
            .Where(m => m.ConversationId == conversationId && m.TurnNumber < turnNumber)
            .OrderByDescending(m => m.TurnNumber).ThenByDescending(m => m.Role)
            .Take(options.Value.ContextTurnLimit)
            .Select(m => new { m.Role, m.Content })
            .ToListAsync(cancellationToken);
        recent.Reverse();
        return new DiscoveryTurnContext(conversationId, conversation.Version, turnNumber,
            conversation.FiltersJson,
            recent.Select(m => new DiscoveryIntentTurn(m.Role.ToString().ToLowerInvariant(), m.Content)).ToList());
    }
}
