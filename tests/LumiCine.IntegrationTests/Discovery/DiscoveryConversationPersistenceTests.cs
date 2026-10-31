using LumiCine.Application.Common;
using LumiCine.Application.Discovery;
using LumiCine.Infrastructure.Discovery;
using LumiCine.Infrastructure.Identity;
using LumiCine.Infrastructure.Persistence;
using LumiCine.IntegrationTests.Persistence;
using Microsoft.Extensions.Options;

namespace LumiCine.IntegrationTests.Discovery;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class DiscoveryConversationPersistenceTests(PostgresDatabaseFixture fixture)
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
            DisplayName = "Discovery test"
        });
        await db.SaveChangesAsync();
        return userId;
    }

    private static DiscoveryConversationService CreateService(LumiCineDbContext db) =>
        DiscoveryTestServices.CreateConversationService(db);

    [Fact]
    public async Task Create_persists_private_conversation_with_default_filters()
    {
        await using var db = fixture.CreateDbContext();
        var userId = await CreateUserAsync(db, $"discovery-create-{Guid.NewGuid():N}@lumicine.test");
        var service = CreateService(db);

        var details = await service.CreateAsync(userId, default);

        Assert.NotEqual(Guid.Empty, details.Id);
        Assert.Equal(1, details.Version);
        Assert.Equal(0, details.TurnCount);
        Assert.False(details.IsExpired);
        Assert.Null(details.Filters.TopicText);
        Assert.Null(details.Filters.MaxPrice);
        Assert.Null(details.Filters.CinemaName);
        Assert.Empty(details.Messages);
    }

    [Fact]
    public async Task List_returns_only_owner_conversations_newest_first()
    {
        await using var db = fixture.CreateDbContext();
        var userId = await CreateUserAsync(db, $"discovery-list-{Guid.NewGuid():N}@lumicine.test");
        var otherId = await CreateUserAsync(db, $"discovery-list-other-{Guid.NewGuid():N}@lumicine.test");
        var service = CreateService(db);

        var first = await service.CreateAsync(userId, default);
        var second = await service.CreateAsync(userId, default);
        await service.CreateAsync(otherId, default);

        var page = await service.ListAsync(userId, new PageQuery(1, 10), default);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal([second.Id, first.Id], page.Items.Select(c => c.Id).ToList());
        Assert.All(page.Items, c => Assert.False(c.IsExpired));
    }

    [Fact]
    public async Task Get_returns_null_for_other_users_conversations()
    {
        await using var db = fixture.CreateDbContext();
        var ownerId = await CreateUserAsync(db, $"discovery-owner-{Guid.NewGuid():N}@lumicine.test");
        var strangerId = await CreateUserAsync(db, $"discovery-stranger-{Guid.NewGuid():N}@lumicine.test");
        var service = CreateService(db);

        var details = await service.CreateAsync(ownerId, default);

        Assert.NotNull(await service.GetAsync(ownerId, details.Id, default));
        Assert.Null(await service.GetAsync(strangerId, details.Id, default));
    }

    [Fact]
    public async Task Inactive_conversations_report_expiry()
    {
        await using var db = fixture.CreateDbContext();
        var userId = await CreateUserAsync(db, $"discovery-expiry-{Guid.NewGuid():N}@lumicine.test");

        var createdAt = new DateTime(2026, 11, 1, 3, 0, 0, DateTimeKind.Utc);
        var conversation = new Domain.Discovery.DiscoveryConversation(Guid.NewGuid(), userId, "{}", createdAt);
        db.DiscoveryConversations.Add(conversation);
        await db.SaveChangesAsync();

        var clock = new FixedClock(new DateTime(2026, 11, 9, 3, 0, 0, DateTimeKind.Utc));
        var service = DiscoveryTestServices.CreateConversationService(fixture.CreateDbContext(), clock);
        var summary = Assert.Single((await service.ListAsync(userId, new PageQuery(1, 10), default)).Items);

        Assert.True(summary.IsExpired);
        Assert.Equal(createdAt.AddDays(7), summary.ExpiresAtUtc);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
