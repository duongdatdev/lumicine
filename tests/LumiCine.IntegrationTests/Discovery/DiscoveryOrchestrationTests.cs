using LumiCine.Application.Discovery;
using LumiCine.Domain.Catalog;
using LumiCine.Domain.Cinemas;
using LumiCine.Domain.Scheduling;
using LumiCine.Infrastructure.Identity;
using LumiCine.Infrastructure.Persistence;
using LumiCine.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LumiCine.IntegrationTests.Discovery;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class DiscoveryOrchestrationTests(PostgresDatabaseFixture fixture)
{
    private static readonly DateTime FixedNow = new(2026, 11, 12, 13, 0, 0, DateTimeKind.Utc);

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
            DisplayName = "Orchestration test"
        });
        await db.SaveChangesAsync();
        return userId;
    }

    private static async Task<(Guid CinemaId, string CinemaName, Guid MovieId, Guid ShowtimeId)> SeedCatalogAsync(
        LumiCineDbContext db, string suffix, params decimal[] seatPrices)
    {
        var now = DateTime.UtcNow;
        var cinema = new Cinema(Guid.NewGuid(), $"OT" + suffix.ToUpperInvariant(), $"Rạp Trung Tâm {suffix}", $"Địa chỉ {suffix}", $"Hà Nội {suffix}", now);
        var room = new Room(Guid.NewGuid(), cinema.Id, $"OT-R" + suffix.ToUpperInvariant(), $"Phòng {suffix}", now);
        var movie = new Movie(Guid.NewGuid(), $"Thời Gian Vô Hình {suffix}", 110, now, isActive: true);
        var showtime = new Showtime(Guid.NewGuid(), movie.Id, room.Id, FixedNow.AddHours(20), FixedNow.AddHours(22), 120_000m, now);
        await db.AddRangeAsync(cinema, room, movie, showtime);
        await db.SaveChangesAsync();

        foreach (var (price, index) in seatPrices.Select((p, i) => (p, i)))
        {
            var seat = new Seat(Guid.NewGuid(), room.Id, "A", index + 1, "Standard", now);
            db.Seats.Add(seat);
            db.ShowtimeSeats.Add(new ShowtimeSeat(Guid.NewGuid(), showtime.Id, room.Id, seat.Id, price, now));
        }
        await db.SaveChangesAsync();
        return (cinema.Id, cinema.Name, movie.Id, showtime.Id);
    }

    private static string AdjustIntent(string suffix, string? cinemaName = null) =>
        $$"""
        {"Intent":"adjust","MovieNameText":"Thời Gian Vô Hình","CinemaName":{{(cinemaName is null ? "null" : $"\"{cinemaName}\"")}},"MaxPrice":120000,"MinSeats":2}
        """;

    [Fact]
    public async Task Adjust_turn_resolves_filters_searches_and_answers()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var db = fixture.CreateDbContext();
        var userId = await CreateUserAsync(db, $"discovery-orch-{Guid.NewGuid():N}@lumicine.test");
        var (cinemaId, cinemaName, movieId, showtimeId) = await SeedCatalogAsync(db, suffix, 100_000m, 120_000m, 150_000m);
        var service = DiscoveryTestServices.CreateConversationService(db, new ScriptedChatClient(
            new ScriptedChatReply(AdjustIntent(suffix, cinemaName)),
            new ScriptedChatReply("Mình tìm thấy phim phù hợp cho bạn ngay đây.")), new FixedClock(FixedNow));
        var conversationId = (await service.CreateAsync(userId, default)).Id;

        var requestId = Guid.NewGuid();
        var response = await service.SendMessageAsync(userId, conversationId,
            new SendDiscoveryMessageRequest(requestId, 1, $"Gợi ý phim Thời Gian Vô Hình {suffix} ở {cinemaName} dưới 120 nghìn đủ 2 ghế."), default);

        Assert.Equal(conversationId, response.ConversationId);
        Assert.Equal(2, response.Version);
        Assert.Equal(1, response.TurnNumber);
        Assert.Equal(requestId, response.RequestId);
        Assert.False(response.IsDegraded);
        Assert.NotEqual(DiscoveryModes.Clarification, response.Mode);
        Assert.Equal(120_000m, response.AppliedFilters.MaxPrice);
        Assert.Equal(2, response.AppliedFilters.MinSeats);
        Assert.Equal(cinemaName, response.AppliedFilters.CinemaName);
        Assert.Equal("Thời Gian Vô Hình", response.AppliedFilters.MovieNameText);
        var result = Assert.Single(response.Movies, m => m.MovieId == movieId);
        var showtime = Assert.Single(result.Showtimes);
        Assert.Equal(showtimeId, showtime.ShowtimeId);
        Assert.Equal(100_000m, showtime.Price);
        Assert.Equal(3, showtime.AvailableSeats);
        Assert.Equal("Mình tìm thấy phim phù hợp cho bạn ngay đây.", response.Answer);

        await using var verify = fixture.CreateDbContext();
        var assistant = await verify.DiscoveryMessages.SingleAsync(
            m => m.ConversationId == conversationId && m.Role == LumiCine.Domain.Discovery.DiscoveryMessageRole.Assistant);
        Assert.Contains(requestId.ToString(), assistant.ResponseJson);
    }

    [Fact]
    public async Task Repeated_payload_replays_the_saved_response()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var db = fixture.CreateDbContext();
        var userId = await CreateUserAsync(db, $"discovery-orch-{Guid.NewGuid():N}@lumicine.test");
        await SeedCatalogAsync(db, suffix, 100_000m);
        var service = DiscoveryTestServices.CreateConversationService(db, new ScriptedChatClient(
            new ScriptedChatReply(AdjustIntent(suffix, $"Rạp Trung Tâm {suffix}")),
            new ScriptedChatReply("Phim này đang có suất, bạn chọn giờ nhé.")), new FixedClock(FixedNow));
        var conversationId = (await service.CreateAsync(userId, default)).Id;
        var requestId = Guid.NewGuid();
        var message = $"Phim Thời Gian Vô Hình {suffix} ở Rạp Trung Tâm {suffix}.";

        var first = await service.SendMessageAsync(userId, conversationId, new(requestId, 1, message), default);
        var replay = await service.SendMessageAsync(userId, conversationId, new(requestId, 1, message), default);

        Assert.Equal(first.Answer, replay.Answer);
        Assert.Equal(first.TurnNumber, replay.TurnNumber);
        Assert.Equal(first.Version, replay.Version);
        Assert.Equal(first.Movies.Count, replay.Movies.Count);
        await using var verify = fixture.CreateDbContext();
        Assert.Equal(2, await verify.DiscoveryMessages.CountAsync(m => m.ConversationId == conversationId));
    }

    [Fact]
    public async Task Ambiguous_cinema_clarifies_without_changing_filters()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var db = fixture.CreateDbContext();
        var userId = await CreateUserAsync(db, $"discovery-orch-{Guid.NewGuid():N}@lumicine.test");
        var service = DiscoveryTestServices.CreateConversationService(db, new ScriptedChatClient(
            new ScriptedChatReply(AdjustIntent(suffix, $"Rạp Không Tồn Tại {suffix}"))), new FixedClock(FixedNow));
        var conversationId = (await service.CreateAsync(userId, default)).Id;

        var response = await service.SendMessageAsync(userId, conversationId,
            new SendDiscoveryMessageRequest(Guid.NewGuid(), 1, $"Xem phim ở Rạp Không Tồn Tại {suffix}."), default);

        Assert.Equal(DiscoveryModes.Clarification, response.Mode);
        Assert.Contains("chưa tìm thấy rạp", response.Answer);
        Assert.Empty(response.Movies);
        Assert.False(response.IsDegraded);
        Assert.Null(response.AppliedFilters.MaxPrice);
        Assert.Null(response.AppliedFilters.CinemaName);
    }

    [Fact]
    public async Task Parse_failure_degrades_to_keyword_search_keeping_filters()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var db = fixture.CreateDbContext();
        var userId = await CreateUserAsync(db, $"discovery-orch-{Guid.NewGuid():N}@lumicine.test");
        var (_, _, movieId, _) = await SeedCatalogAsync(db, suffix, 100_000m);
        var service = DiscoveryTestServices.CreateConversationService(db, new ScriptedChatClient(
            new ScriptedChatReply(Failure: new HttpRequestException("503 upstream"))), new FixedClock(FixedNow));
        var conversationId = (await service.CreateAsync(userId, default)).Id;

        var response = await service.SendMessageAsync(userId, conversationId,
            new SendDiscoveryMessageRequest(Guid.NewGuid(), 1, $"Tôi muốn xem Thời Gian Vô Hình {suffix}."), default);

        Assert.True(response.IsDegraded);
        Assert.Contains(response.Warnings, w => w.Contains("từ khóa"));
        Assert.NotNull(Assert.Single(response.Movies, m => m.MovieId == movieId));
        Assert.Null(response.AppliedFilters.MaxPrice);
        Assert.Null(response.AppliedFilters.MovieNameText);
    }

    [Fact]
    public async Task Writer_failure_falls_back_to_the_results_template()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var db = fixture.CreateDbContext();
        var userId = await CreateUserAsync(db, $"discovery-orch-{Guid.NewGuid():N}@lumicine.test");
        await SeedCatalogAsync(db, suffix, 100_000m, 150_000m);
        var service = DiscoveryTestServices.CreateConversationService(db, new ScriptedChatClient(
            new ScriptedChatReply(AdjustIntent(suffix, $"Rạp Trung Tâm {suffix}")),
            new ScriptedChatReply(Failure: new HttpRequestException("writer down"))), new FixedClock(FixedNow));
        var conversationId = (await service.CreateAsync(userId, default)).Id;

        var response = await service.SendMessageAsync(userId, conversationId,
            new SendDiscoveryMessageRequest(Guid.NewGuid(), 1, $"Phim Thời Gian Vô Hình {suffix} ở Rạp Trung Tâm {suffix}."), default);

        Assert.True(response.IsDegraded);
        Assert.Contains(response.Warnings, w => w.Contains("câu mẫu"));
        Assert.Contains("Thời Gian Vô Hình", response.Answer);
        Assert.Contains("100.000đ", response.Answer);
        Assert.NotEmpty(response.Movies);
    }

    [Fact]
    public async Task Invalid_answer_references_fall_back_to_the_template()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var db = fixture.CreateDbContext();
        var userId = await CreateUserAsync(db, $"discovery-orch-{Guid.NewGuid():N}@lumicine.test");
        await SeedCatalogAsync(db, suffix, 100_000m);
        var service = DiscoveryTestServices.CreateConversationService(db, new ScriptedChatClient(
            new ScriptedChatReply(AdjustIntent(suffix, $"Rạp Trung Tâm {suffix}")),
            new ScriptedChatReply("Xem ngay [movie:11111111-1111-1111-1111-111111111111] nhé.")), new FixedClock(FixedNow));
        var conversationId = (await service.CreateAsync(userId, default)).Id;

        var response = await service.SendMessageAsync(userId, conversationId,
            new SendDiscoveryMessageRequest(Guid.NewGuid(), 1, $"Phim Thời Gian Vô Hình {suffix}."), default);

        Assert.True(response.IsDegraded);
        Assert.Contains(response.Warnings, w => w.Contains("tham chiếu"));
        Assert.DoesNotContain("11111111", response.Answer);
        Assert.Contains("Thời Gian Vô Hình", response.Answer);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
