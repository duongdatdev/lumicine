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
public sealed class DiscoveryTurnStoreTests(PostgresDatabaseFixture fixture)
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
            DisplayName = "Turn test"
        });
        await db.SaveChangesAsync();
        return userId;
    }

    private static DiscoveryConversationTurnStore CreateStore(LumiCineDbContext db, TimeProvider? clock = null) =>
        new(db, clock ?? TimeProvider.System, Options.Create(new DiscoveryConversationOptions()));

    private static DiscoveryChatResponse SampleResponse(Guid conversationId, Guid requestId, int version, int turnNumber) => new(
        conversationId, version, requestId, turnNumber,
        "Đây là một số phim phù hợp.",
        new DiscoveryFiltersView(null, null, [], null, null, null, null, null, null, null, null, null),
        [],
        DiscoveryModes.Schedule, false, [], DateTime.UtcNow, DateTime.UtcNow);

    [Fact]
    public async Task Begin_appends_user_message_and_bumps_version_and_turn()
    {
        await using var db = fixture.CreateDbContext();
        var userId = await CreateUserAsync(db, $"turn-begin-{Guid.NewGuid():N}@lumicine.test");
        var conversationId = (await DiscoveryTestServices.CreateConversationService(db).CreateAsync(userId, default)).Id;

        var store = CreateStore(db);
        var result = await store.BeginAsync(userId, conversationId, new(Guid.NewGuid(), 1, "Có phim du hành thời gian không?"), default);

        Assert.NotNull(result.Context);
        Assert.Null(result.ReplayedResponse);
        Assert.Equal(2, result.Context!.Version);
        Assert.Equal(1, result.Context.TurnNumber);
        Assert.Equal([], result.Context.RecentTurns);

        await using var verify = fixture.CreateDbContext();
        Assert.Equal(1, (await verify.DiscoveryConversations.SingleAsync(c => c.Id == conversationId)).TurnCount);
        var userMessage = Assert.Single(await verify.DiscoveryMessages.Where(m => m.ConversationId == conversationId).ToListAsync());
        Assert.Equal("Có phim du hành thời gian không?", userMessage.Content);
    }

    [Fact]
    public async Task Complete_stores_response_and_filter_snapshot()
    {
        await using var db = fixture.CreateDbContext();
        var userId = await CreateUserAsync(db, $"turn-complete-{Guid.NewGuid():N}@lumicine.test");
        var conversationId = (await DiscoveryTestServices.CreateConversationService(db).CreateAsync(userId, default)).Id;
        var requestId = Guid.NewGuid();
        var store = CreateStore(db);
        var context = (await store.BeginAsync(userId, conversationId, new(requestId, 1, "Phim dưới 2 tiếng"), default)).Context!;

        var updatedFilters = new DiscoveryFilters { MovieNameText = "Interstellar", MaxPrice = 120_000m };
        var response = SampleResponse(conversationId, requestId, context.Version, context.TurnNumber);
        await store.CompleteAsync(userId, conversationId, context.TurnNumber, updatedFilters.Serialize(), response, "Mình tìm thấy Interstellar.", default);

        await using var verify = fixture.CreateDbContext();
        var conversation = await verify.DiscoveryConversations.SingleAsync(c => c.Id == conversationId);
        Assert.Equal(2, conversation.Version);
        Assert.Equal(1, conversation.TurnCount);
        Assert.Contains("Interstellar", conversation.FiltersJson);
        var assistant = await verify.DiscoveryMessages.SingleAsync(m => m.ConversationId == conversationId && m.Role == Domain.Discovery.DiscoveryMessageRole.Assistant);
        Assert.Equal("Mình tìm thấy Interstellar.", assistant.Content);
        Assert.Contains(requestId.ToString(), assistant.ResponseJson);
    }

    [Fact]
    public async Task Repeated_request_with_same_payload_replays_saved_response()
    {
        await using var db = fixture.CreateDbContext();
        var userId = await CreateUserAsync(db, $"turn-replay-{Guid.NewGuid():N}@lumicine.test");
        var conversationId = (await DiscoveryTestServices.CreateConversationService(db).CreateAsync(userId, default)).Id;
        var requestId = Guid.NewGuid();
        var store = CreateStore(db);
        var context = (await store.BeginAsync(userId, conversationId, new(requestId, 1, "Có phim kinh dị không?"), default)).Context!;
        var response = SampleResponse(conversationId, requestId, context.Version, context.TurnNumber);
        await store.CompleteAsync(userId, conversationId, context.TurnNumber, DiscoveryFilters.Empty().Serialize(), response, "Có một phim.", default);

        var replay = await store.BeginAsync(userId, conversationId, new(requestId, 1, "Có phim kinh dị không?"), default);

        Assert.Null(replay.Context);
        Assert.NotNull(replay.ReplayedResponse);
        Assert.Equal(response.ConversationId, replay.ReplayedResponse!.ConversationId);
        Assert.Equal(response.TurnNumber, replay.ReplayedResponse.TurnNumber);

        await using var verify = fixture.CreateDbContext();
        Assert.Equal(2, (await verify.DiscoveryConversations.SingleAsync(c => c.Id == conversationId)).Version);
        Assert.Equal(2, await verify.DiscoveryMessages.CountAsync(m => m.ConversationId == conversationId));
    }

    [Fact]
    public async Task Repeated_request_with_different_payload_conflicts()
    {
        await using var db = fixture.CreateDbContext();
        var userId = await CreateUserAsync(db, $"turn-replay-conflict-{Guid.NewGuid():N}@lumicine.test");
        var conversationId = (await DiscoveryTestServices.CreateConversationService(db).CreateAsync(userId, default)).Id;
        var requestId = Guid.NewGuid();
        var store = CreateStore(db);
        var context = (await store.BeginAsync(userId, conversationId, new(requestId, 1, "Phim hài"), default)).Context!;
        await store.CompleteAsync(userId, conversationId, context.TurnNumber, DiscoveryFilters.Empty().Serialize(), SampleResponse(conversationId, requestId, context.Version, context.TurnNumber), "Có phim hài.", default);

        var conflict = await Assert.ThrowsAsync<ApiException>(() =>
            store.BeginAsync(userId, conversationId, new(requestId, 1, "Phim hành động"), default));
        Assert.Equal(409, conflict.StatusCode);
    }

    [Fact]
    public async Task Stale_expected_version_conflicts()
    {
        await using var db = fixture.CreateDbContext();
        var userId = await CreateUserAsync(db, $"turn-version-{Guid.NewGuid():N}@lumicine.test");
        var conversationId = (await DiscoveryTestServices.CreateConversationService(db).CreateAsync(userId, default)).Id;
        var store = CreateStore(db);
        await store.BeginAsync(userId, conversationId, new(Guid.NewGuid(), 1, "Lượt một"), default);

        var stale = await Assert.ThrowsAsync<ApiException>(() =>
            store.BeginAsync(userId, conversationId, new(Guid.NewGuid(), 1, "Lượt hai"), default));
        Assert.Equal(409, stale.StatusCode);

        var fresh = await store.BeginAsync(userId, conversationId, new(Guid.NewGuid(), 2, "Lượt hai"), default);
        Assert.Equal(3, fresh.Context!.Version);
        Assert.Equal(2, fresh.Context.TurnNumber);
    }

    [Fact]
    public async Task Expired_conversation_is_rejected()
    {
        await using var db = fixture.CreateDbContext();
        var userId = await CreateUserAsync(db, $"turn-expired-{Guid.NewGuid():N}@lumicine.test");
        var conversation = new Domain.Discovery.DiscoveryConversation(Guid.NewGuid(), userId, "{}", new DateTime(2026, 11, 1, 2, 0, 0, DateTimeKind.Utc));
        db.DiscoveryConversations.Add(conversation);
        await db.SaveChangesAsync();

        var clock = new FixedClock(new DateTime(2026, 11, 9, 2, 0, 0, DateTimeKind.Utc));
        var store = CreateStore(fixture.CreateDbContext(), clock);

        var expired = await Assert.ThrowsAsync<ApiException>(() =>
            store.BeginAsync(userId, conversation.Id, new(Guid.NewGuid(), 1, "Còn suất không?"), default));
        Assert.Equal(410, expired.StatusCode);
    }

    [Fact]
    public async Task Turn_limit_stops_new_turns()
    {
        await using var db = fixture.CreateDbContext();
        var userId = await CreateUserAsync(db, $"turn-limit-{Guid.NewGuid():N}@lumicine.test");
        var conversationId = (await DiscoveryTestServices.CreateConversationService(db).CreateAsync(userId, default)).Id;
        var store = CreateStore(db);

        var version = 1;
        for (var turn = 1; turn <= 20; turn++)
        {
            var context = (await store.BeginAsync(userId, conversationId, new(Guid.NewGuid(), version, $"Lượt {turn}"), default)).Context!;
            await store.CompleteAsync(userId, conversationId, context.TurnNumber, DiscoveryFilters.Empty().Serialize(), SampleResponse(conversationId, Guid.NewGuid(), context.Version, context.TurnNumber), "Trả lời.", default);
            version = context.Version;
        }

        var rejected = await Assert.ThrowsAsync<ApiException>(() =>
            store.BeginAsync(userId, conversationId, new(Guid.NewGuid(), version, "Lượt 21"), default));
        Assert.Equal(409, rejected.StatusCode);
    }

    [Fact]
    public async Task Other_users_and_invalid_input_are_rejected()
    {
        await using var db = fixture.CreateDbContext();
        var ownerId = await CreateUserAsync(db, $"turn-owner-{Guid.NewGuid():N}@lumicine.test");
        var strangerId = await CreateUserAsync(db, $"turn-stranger-{Guid.NewGuid():N}@lumicine.test");
        var conversationId = (await DiscoveryTestServices.CreateConversationService(db).CreateAsync(ownerId, default)).Id;
        var store = CreateStore(db);

        var foreign = await Assert.ThrowsAsync<ApiException>(() =>
            store.BeginAsync(strangerId, conversationId, new(Guid.NewGuid(), 1, "Xem phim"), default));
        Assert.Equal(404, foreign.StatusCode);

        var empty = await Assert.ThrowsAsync<ApiException>(() =>
            store.BeginAsync(ownerId, conversationId, new(Guid.NewGuid(), 1, "   "), default));
        Assert.Equal(400, empty.StatusCode);

        var tooLong = await Assert.ThrowsAsync<ApiException>(() =>
            store.BeginAsync(ownerId, conversationId, new(Guid.NewGuid(), 1, new string('x', 1001)), default));
        Assert.Equal(400, tooLong.StatusCode);

        var completion = await Assert.ThrowsAsync<ApiException>(() =>
            store.CompleteAsync(strangerId, conversationId, 1, "{}", SampleResponse(conversationId, Guid.NewGuid(), 2, 1), "Trả lời", default));
        Assert.Equal(404, completion.StatusCode);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
