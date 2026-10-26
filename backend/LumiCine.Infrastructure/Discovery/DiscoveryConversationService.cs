using LumiCine.Application.Common;
using LumiCine.Application.Discovery;
using LumiCine.Domain.Discovery;
using LumiCine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LumiCine.Infrastructure.Discovery;

public sealed class DiscoveryConversationService(
    LumiCineDbContext db,
    TimeProvider clock,
    IOptions<DiscoveryConversationOptions> options) : IDiscoveryConversationService
{
    private TimeSpan InactivityWindow => TimeSpan.FromDays(options.Value.InactivityDays);
    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    public async Task<DiscoveryConversationDetails> CreateAsync(Guid userId, CancellationToken cancellationToken)
    {
        var conversation = new DiscoveryConversation(Guid.NewGuid(), userId, DiscoveryFilters.Empty().Serialize(), UtcNow);
        db.DiscoveryConversations.Add(conversation);
        await db.SaveChangesAsync(cancellationToken);
        return await LoadDetailsAsync(userId, conversation.Id, cancellationToken)
            ?? throw new InvalidOperationException("Conversation disappeared right after creation.");
    }

    public async Task<PagedResult<DiscoveryConversationSummary>> ListAsync(Guid userId, PageQuery page, CancellationToken cancellationToken)
    {
        page.Validate();
        var now = UtcNow;
        var expiryThreshold = now - InactivityWindow;
        var query = db.DiscoveryConversations.AsNoTracking().Where(c => c.UserId == userId)
            .OrderByDescending(c => c.LastActiveAtUtc).ThenByDescending(c => c.Id);
        var total = await query.CountAsync(cancellationToken);
        var items = page.Offset > int.MaxValue ? [] : await query.Skip((int)page.Offset).Take(page.PageSize)
            .Select(c => new DiscoveryConversationSummary(
                c.Id, c.Version, c.TurnCount, c.LastActiveAtUtc <= expiryThreshold,
                c.CreatedAtUtc, c.LastActiveAtUtc, c.LastActiveAtUtc.AddDays(options.Value.InactivityDays)))
            .ToListAsync(cancellationToken);
        return new(items, total, page.Page, page.PageSize);
    }

    public async Task<DiscoveryConversationDetails?> GetAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken) =>
        await LoadDetailsAsync(userId, conversationId, cancellationToken);

    private async Task<DiscoveryConversationDetails?> LoadDetailsAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken)
    {
        var now = UtcNow;
        var conversation = await db.DiscoveryConversations.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId, cancellationToken);
        if (conversation is null) return null;

        var messages = await db.DiscoveryMessages.AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.TurnNumber).ThenBy(m => m.Role)
            .Select(m => new DiscoveryMessageView(m.TurnNumber, m.Role.ToString(), m.Content, m.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        var filters = DiscoveryFilters.Parse(conversation.FiltersJson);
        return new(conversation.Id, conversation.Version, conversation.TurnCount,
            conversation.IsExpired(now, InactivityWindow), filters.ToView(), messages,
            conversation.CreatedAtUtc, conversation.LastActiveAtUtc, conversation.ExpiresAtUtc(InactivityWindow));
    }
}
