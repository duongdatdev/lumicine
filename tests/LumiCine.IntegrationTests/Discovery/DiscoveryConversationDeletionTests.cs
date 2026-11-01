using LumiCine.Application.Common;
using LumiCine.Application.Discovery;
using LumiCine.Infrastructure.Discovery;
using LumiCine.Infrastructure.Identity;
using LumiCine.Infrastructure.Persistence;
using LumiCine.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LumiCine.IntegrationTests.Discovery;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class DiscoveryConversationDeletionTests(PostgresDatabaseFixture fixture)
{
    private static async Task<Guid> CreateUserAsync(LumiCineDbContext db, string email)
    {
        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser
        {
            Id = userId,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            IsActive = true,
            DisplayName = "Deletion test"
        });
        await db.SaveChangesAsync();
        return userId;
    }

    private static DiscoveryConversationService CreateService(LumiCineDbContext db, TimeProvider? clock = null) =>
        DiscoveryTestServices.CreateConversationService(db, clock);

    private async Task SeedCompletedTurnAsync(Guid conversationId)
    {
        // Seeded through its own context: a request-scoped service never sees these tracked entities.
        await using var db = fixture.CreateDbContext();
        var now = DateTime.UtcNow;
        db.DiscoveryMessages.Add(new Domain.Discovery.DiscoveryMessage(Guid.NewGuid(), conversationId, 1,
            Domain.Discovery.DiscoveryMessageRole.User, "Có phim hành động nào hay không?", now, requestId: Guid.NewGuid()));
        db.DiscoveryMessages.Add(new Domain.Discovery.DiscoveryMessage(Guid.NewGuid(), conversationId, 1,
            Domain.Discovery.DiscoveryMessageRole.Assistant, "Có một vài phim hành động.", now, responseJson: "{}"));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Owner_deletes_conversation_with_completed_turn()
    {
        await using var db = fixture.CreateDbContext();
        var userId = await CreateUserAsync(db, $"discovery-delete-{Guid.NewGuid():N}@lumicine.test");
        var service = CreateService(db);
        var conversationId = (await service.CreateAsync(userId, default)).Id;
        await SeedCompletedTurnAsync(conversationId);

        var deleted = await service.DeleteAsync(userId, conversationId, default);

        Assert.True(deleted);
        await using var verify = fixture.CreateDbContext();
        Assert.Null(await CreateService(verify).GetAsync(userId, conversationId, default));
        Assert.Equal(0, await verify.DiscoveryMessages.CountAsync(m => m.ConversationId == conversationId));
    }

    [Fact]
    public async Task Stranger_delete_is_rejected_and_data_survives()
    {
        await using var db = fixture.CreateDbContext();
        var ownerId = await CreateUserAsync(db, $"discovery-del-owner-{Guid.NewGuid():N}@lumicine.test");
        var strangerId = await CreateUserAsync(db, $"discovery-del-stranger-{Guid.NewGuid():N}@lumicine.test");
        var service = CreateService(db);
        var conversationId = (await service.CreateAsync(ownerId, default)).Id;
        await SeedCompletedTurnAsync(conversationId);

        var deleted = await service.DeleteAsync(strangerId, conversationId, default);

        Assert.False(deleted);
        await using var verify = fixture.CreateDbContext();
        Assert.Equal(1, await verify.DiscoveryConversations.CountAsync(c => c.Id == conversationId));
        Assert.Equal(2, await verify.DiscoveryMessages.CountAsync(m => m.ConversationId == conversationId));
        Assert.NotNull(await CreateService(verify).GetAsync(ownerId, conversationId, default));
    }

    [Fact]
    public async Task Unknown_conversation_delete_returns_false()
    {
        await using var db = fixture.CreateDbContext();
        var userId = await CreateUserAsync(db, $"discovery-del-missing-{Guid.NewGuid():N}@lumicine.test");
        var service = CreateService(db);

        Assert.False(await service.DeleteAsync(userId, Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Expired_conversation_still_deletes()
    {
        await using var db = fixture.CreateDbContext();
        var userId = await CreateUserAsync(db, $"discovery-del-expired-{Guid.NewGuid():N}@lumicine.test");
        var conversation = new Domain.Discovery.DiscoveryConversation(Guid.NewGuid(), userId, "{}",
            DateTime.UtcNow.AddDays(-8));
        db.DiscoveryConversations.Add(conversation);
        await db.SaveChangesAsync();

        var deleted = await CreateService(fixture.CreateDbContext(), TimeProvider.System)
            .DeleteAsync(userId, conversation.Id, default);

        Assert.True(deleted);
        await using var verify = fixture.CreateDbContext();
        Assert.Equal(0, await verify.DiscoveryConversations.CountAsync(c => c.Id == conversation.Id));
    }

    [Fact]
    public async Task Begin_on_deleted_conversation_is_not_found()
    {
        await using var db = fixture.CreateDbContext();
        var userId = await CreateUserAsync(db, $"discovery-del-begin-{Guid.NewGuid():N}@lumicine.test");
        var service = CreateService(db);
        var conversationId = (await service.CreateAsync(userId, default)).Id;
        Assert.True(await service.DeleteAsync(userId, conversationId, default));

        var store = new DiscoveryConversationTurnStore(fixture.CreateDbContext(), TimeProvider.System,
            Options.Create(new DiscoveryConversationOptions()));

        var missing = await Assert.ThrowsAsync<ApiException>(() =>
            store.BeginAsync(userId, conversationId, new(Guid.NewGuid(), 1, "Còn lịch chiếu không?"), default));
        Assert.Equal(404, missing.StatusCode);
    }
}
