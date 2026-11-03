using LumiCine.Application.Common;
using LumiCine.Application.Discovery;
using LumiCine.Domain.Catalog;
using LumiCine.Domain.Cinemas;
using LumiCine.Domain.Discovery;
using LumiCine.Domain.Scheduling;
using LumiCine.Infrastructure.Discovery;
using LumiCine.Infrastructure.Identity;
using LumiCine.Infrastructure.Persistence;
using LumiCine.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LumiCine.IntegrationTests.Discovery;

/// <summary>
/// Multi-turn conversational search scenarios over the real orchestration services with a
/// scripted AI: parser reply first, writer reply second, one fresh conversation per scenario.
/// </summary>
[Collection(PostgresDatabaseCollection.Name)]
public sealed class DiscoveryConversationScenarioTests(PostgresDatabaseFixture fixture)
{
    // Saturday 10:00 in UTC+7: the zone-local "today" is 2026-11-14 while UTC is still the 14th early morning.
    private static readonly DateTime FixedNow = new(2026, 11, 14, 3, 0, 0, DateTimeKind.Utc);

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
            DisplayName = "Scenario test"
        });
        await db.SaveChangesAsync();
        return userId;
    }

    private sealed record CatalogSeed(
        Guid CinemaId,
        string CinemaName,
        Guid RoomId,
        Guid MovieId,
        Guid ShowtimeId);

    private static async Task<CatalogSeed> SeedCatalogAsync(
        LumiCineDbContext db, string suffix, DateTime showtimeStartUtc, params decimal[] seatPrices)
    {
        var now = DateTime.UtcNow;
        var cinema = new Cinema(Guid.NewGuid(), $"SC" + suffix.ToUpperInvariant(), $"Rạp Scenario {suffix}", $"Địa chỉ {suffix}", $"Hà Nội {suffix}", now);
        var room = new Room(Guid.NewGuid(), cinema.Id, $"SC-R" + suffix.ToUpperInvariant(), $"Phòng {suffix}", now);
        var movie = new Movie(Guid.NewGuid(), $"Thời Gian Vô Hình {suffix}", 110, now, isActive: true);
        var showtime = new Showtime(Guid.NewGuid(), movie.Id, room.Id, showtimeStartUtc, showtimeStartUtc.AddHours(2), 120_000m, now);
        await db.AddRangeAsync(cinema, room, movie, showtime);
        await db.SaveChangesAsync();

        await SeedShowtimeSeatsAsync(db, showtime, "A", seatPrices);
        return new CatalogSeed(cinema.Id, cinema.Name, room.Id, movie.Id, showtime.Id);
    }

    private static async Task SeedShowtimeSeatsAsync(LumiCineDbContext db, Showtime showtime, string rowLabel, params decimal[] seatPrices)
    {
        var now = DateTime.UtcNow;
        foreach (var (price, index) in seatPrices.Select((p, i) => (p, i)))
        {
            var seat = new Seat(Guid.NewGuid(), showtime.RoomId, rowLabel, index + 1, "Standard", now);
            db.Seats.Add(seat);
            db.ShowtimeSeats.Add(new ShowtimeSeat(Guid.NewGuid(), showtime.Id, showtime.RoomId, seat.Id, price, now));
        }
        await db.SaveChangesAsync();
    }

    private static async Task<(Guid MovieId, Guid ShowtimeId)> SeedMovieShowtimeAsync(
        LumiCineDbContext db, string title, Guid roomId, DateTime startUtc, string rowLabel, params decimal[] seatPrices)
    {
        var now = DateTime.UtcNow;
        var movie = new Movie(Guid.NewGuid(), title, 105, now, isActive: true);
        var showtime = new Showtime(Guid.NewGuid(), movie.Id, roomId, startUtc, startUtc.AddHours(2), 120_000m, now);
        await db.AddRangeAsync(movie, showtime);
        await db.SaveChangesAsync();
        await SeedShowtimeSeatsAsync(db, showtime, rowLabel, seatPrices);
        return (movie.Id, showtime.Id);
    }

    private static async Task<Guid> SeedExtraShowtimeAsync(
        LumiCineDbContext db, Guid movieId, Guid roomId, DateTime startUtc, string rowLabel, params decimal[] seatPrices)
    {
        var showtime = new Showtime(Guid.NewGuid(), movieId, roomId, startUtc, startUtc.AddHours(2), 120_000m, DateTime.UtcNow);
        db.Showtimes.Add(showtime);
        await db.SaveChangesAsync();
        await SeedShowtimeSeatsAsync(db, showtime, rowLabel, seatPrices);
        return showtime.Id;
    }

    private static async Task<(Guid GenreId, string Slug)> SeedGenreAsync(LumiCineDbContext db, string suffix)
    {
        var slug = $"khoa-hoc-vien-tuong-{suffix}";
        var genre = new Genre(Guid.NewGuid(), $"Khoa Học Viễn Tưởng {suffix}", slug, DateTime.UtcNow);
        db.Genres.Add(genre);
        await db.SaveChangesAsync();
        return (genre.Id, genre.Slug);
    }

    private static async Task AttachGenreAsync(LumiCineDbContext db, Guid movieId, Guid genreId)
    {
        db.MovieGenres.Add(new MovieGenre(movieId, genreId, DateTime.UtcNow));
        await db.SaveChangesAsync();
    }

    private static DiscoveryConversationService CreateService(LumiCineDbContext db, params ScriptedChatReply[] replies) =>
        DiscoveryTestServices.CreateConversationService(db, new ScriptedChatClient(replies), new FixedClock(FixedNow));

    private static async Task AssertAssistantRowAsync(LumiCineDbContext db, Guid conversationId, int turnNumber, string expectedContent)
    {
        var assistant = await db.DiscoveryMessages.AsNoTracking().SingleAsync(m =>
            m.ConversationId == conversationId && m.TurnNumber == turnNumber && m.Role == DiscoveryMessageRole.Assistant);
        Assert.Equal(expectedContent, assistant.Content);
    }

    [Fact]
    public async Task Date_change_then_cinema_change_keeps_inherited_conditions()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var db = fixture.CreateDbContext();
        var userId = await CreateUserAsync(db, $"discovery-scenario-date-{Guid.NewGuid():N}@lumicine.test");
        var seed = await SeedCatalogAsync(db, suffix, FixedNow.AddHours(20), 100_000m, 120_000m, 150_000m);
        var (genreId, genreSlug) = await SeedGenreAsync(db, suffix);
        await AttachGenreAsync(db, seed.MovieId, genreId);
        var service = CreateService(db,
            new ScriptedChatReply($$"""{"Intent":"new_search","GenreSlugs":["{{genreSlug}}"],"MaxPrice":120000}"""),
            new ScriptedChatReply("Mình tìm thấy phim thể loại này đang có suất cho bạn."),
            new ScriptedChatReply("""{"Intent":"adjust","DateFrom":"2026-11-14","DateTo":"2026-11-15"}"""),
            new ScriptedChatReply("Đã cập nhật khoảng ngày cuối tuần cho bạn."),
            new ScriptedChatReply($$"""{"Intent":"adjust","CinemaName":"{{seed.CinemaName}}"}"""),
            new ScriptedChatReply("Đã chuyển điều kiện sang rạp bạn chọn."));
        var conversationId = (await service.CreateAsync(userId, default)).Id;

        var first = await service.SendMessageAsync(userId, conversationId,
            new SendDiscoveryMessageRequest(Guid.NewGuid(), 1, $"Cho mình phim {genreSlug} dưới 120 nghìn."), default);
        Assert.Equal(2, first.Version);
        Assert.Equal([genreSlug], first.AppliedFilters.GenreSlugs);
        Assert.Equal(120_000m, first.AppliedFilters.MaxPrice);
        Assert.Null(first.AppliedFilters.DateFrom);
        Assert.Null(first.AppliedFilters.CinemaName);
        Assert.NotEmpty(first.Movies.Where(m => m.MovieId == seed.MovieId));
        await AssertAssistantRowAsync(db, conversationId, 1, "Mình tìm thấy phim thể loại này đang có suất cho bạn.");

        var second = await service.SendMessageAsync(userId, conversationId,
            new SendDiscoveryMessageRequest(Guid.NewGuid(), 2, $"Xem {genreSlug} từ 14/11 đến 15/11 nhé."), default);
        Assert.Equal(3, second.Version);
        Assert.Equal([genreSlug], second.AppliedFilters.GenreSlugs);
        Assert.Equal(120_000m, second.AppliedFilters.MaxPrice);
        Assert.Equal(new DateOnly(2026, 11, 14), second.AppliedFilters.DateFrom);
        Assert.Equal(new DateOnly(2026, 11, 15), second.AppliedFilters.DateTo);
        Assert.Null(second.AppliedFilters.CinemaName);
        Assert.NotEmpty(second.Movies.Where(m => m.MovieId == seed.MovieId));
        await AssertAssistantRowAsync(db, conversationId, 2, "Đã cập nhật khoảng ngày cuối tuần cho bạn.");

        var third = await service.SendMessageAsync(userId, conversationId,
            new SendDiscoveryMessageRequest(Guid.NewGuid(), 3, $"Đổi sang rạp {seed.CinemaName} thôi."), default);
        Assert.Equal(4, third.Version);
        Assert.Equal([genreSlug], third.AppliedFilters.GenreSlugs);
        Assert.Equal(120_000m, third.AppliedFilters.MaxPrice);
        Assert.Equal(new DateOnly(2026, 11, 14), third.AppliedFilters.DateFrom);
        Assert.Equal(new DateOnly(2026, 11, 15), third.AppliedFilters.DateTo);
        Assert.Equal(seed.CinemaName, third.AppliedFilters.CinemaName);
        Assert.NotEmpty(third.Movies.Where(m => m.MovieId == seed.MovieId));
        Assert.NotEqual(DiscoveryModes.Clarification, third.Mode);
        await AssertAssistantRowAsync(db, conversationId, 3, "Đã chuyển điều kiện sang rạp bạn chọn.");

        await using var verify = fixture.CreateDbContext();
        Assert.Equal(6, await verify.DiscoveryMessages.CountAsync(m => m.ConversationId == conversationId));
        var persisted = (await DiscoveryTestServices.CreateConversationService(verify).GetAsync(userId, conversationId, default))!;
        Assert.Equal(4, persisted.Version);
        Assert.Equal(3, persisted.TurnCount);
        Assert.Equal([genreSlug], persisted.Filters.GenreSlugs);
        Assert.Equal(120_000m, persisted.Filters.MaxPrice);
        Assert.Equal(new DateOnly(2026, 11, 14), persisted.Filters.DateFrom);
        Assert.Equal(new DateOnly(2026, 11, 15), persisted.Filters.DateTo);
        Assert.Equal(seed.CinemaName, persisted.Filters.CinemaName);
    }

    [Fact]
    public async Task Clearing_conditions_only_drops_the_named_fields()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var db = fixture.CreateDbContext();
        var userId = await CreateUserAsync(db, $"discovery-scenario-clear-{Guid.NewGuid():N}@lumicine.test");
        var seed = await SeedCatalogAsync(db, suffix, FixedNow.AddHours(20), 100_000m, 120_000m, 150_000m);
        var (genreId, genreSlug) = await SeedGenreAsync(db, suffix);
        await AttachGenreAsync(db, seed.MovieId, genreId);
        var service = CreateService(db,
            new ScriptedChatReply($$"""{"Intent":"new_search","GenreSlugs":["{{genreSlug}}"],"MaxPrice":120000,"MinSeats":2}"""),
            new ScriptedChatReply("Mình đã ghi nhận các điều kiện đầu tiên."),
            new ScriptedChatReply("""{"Intent":"adjust","MovieNameText":"Thời Gian Vô Hình"}"""),
            new ScriptedChatReply("Đã thêm tên phim vào bộ lọc."),
            new ScriptedChatReply("""{"Intent":"adjust","ClearFields":["maxPrice","minSeats"]}"""),
            new ScriptedChatReply("Đã bỏ hai điều kiện bạn yêu cầu."));
        var conversationId = (await service.CreateAsync(userId, default)).Id;

        var first = await service.SendMessageAsync(userId, conversationId,
            new SendDiscoveryMessageRequest(Guid.NewGuid(), 1, $"Tìm phim {genreSlug} dưới 120 nghìn đủ 2 ghế."), default);
        Assert.Equal(2, first.Version);
        Assert.Equal(120_000m, first.AppliedFilters.MaxPrice);
        Assert.Equal(2, first.AppliedFilters.MinSeats);
        Assert.Equal([genreSlug], first.AppliedFilters.GenreSlugs);
        Assert.Null(first.AppliedFilters.MovieNameText);

        var second = await service.SendMessageAsync(userId, conversationId,
            new SendDiscoveryMessageRequest(Guid.NewGuid(), 2, "Nhớ tên phim Thời Gian Vô Hình giúp mình."), default);
        Assert.Equal(3, second.Version);
        Assert.Equal("Thời Gian Vô Hình", second.AppliedFilters.MovieNameText);
        Assert.Equal(120_000m, second.AppliedFilters.MaxPrice);
        Assert.Equal(2, second.AppliedFilters.MinSeats);
        Assert.Equal([genreSlug], second.AppliedFilters.GenreSlugs);

        var third = await service.SendMessageAsync(userId, conversationId,
            new SendDiscoveryMessageRequest(Guid.NewGuid(), 3, "Bỏ giới hạn giá và số ghế tối thiểu nhé."), default);
        Assert.Equal(4, third.Version);
        Assert.Null(third.AppliedFilters.MaxPrice);
        Assert.Null(third.AppliedFilters.MinSeats);
        Assert.Equal([genreSlug], third.AppliedFilters.GenreSlugs);
        Assert.Equal("Thời Gian Vô Hình", third.AppliedFilters.MovieNameText);
        Assert.Null(third.AppliedFilters.DateFrom);
        Assert.Null(third.AppliedFilters.City);

        await using var verify = fixture.CreateDbContext();
        var persisted = (await DiscoveryTestServices.CreateConversationService(verify).GetAsync(userId, conversationId, default))!;
        Assert.Null(persisted.Filters.MaxPrice);
        Assert.Null(persisted.Filters.MinSeats);
        Assert.Equal([genreSlug], persisted.Filters.GenreSlugs);
        Assert.Equal("Thời Gian Vô Hình", persisted.Filters.MovieNameText);
    }

    [Fact]
    public async Task New_search_intent_resets_state_and_defaults_to_seven_days()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var db = fixture.CreateDbContext();
        var userId = await CreateUserAsync(db, $"discovery-scenario-reset-{Guid.NewGuid():N}@lumicine.test");
        var seed = await SeedCatalogAsync(db, suffix, FixedNow.AddHours(20), 100_000m, 120_000m, 150_000m);
        var newTitle = $"Bố Già {suffix}";
        var (_, nearShowtimeId) = await SeedMovieShowtimeAsync(db, newTitle, seed.RoomId, FixedNow.AddHours(2), "B", 90_000m, 100_000m);
        await SeedMovieShowtimeAsync(db, newTitle, seed.RoomId, FixedNow.AddDays(8).AddHours(2), "C", 90_000m);
        var service = CreateService(db,
            new ScriptedChatReply($$"""{"Intent":"adjust","MaxPrice":150000,"MinSeats":3,"CinemaName":"{{seed.CinemaName}}"}"""),
            new ScriptedChatReply("Đã ghi nhận rạp, giá và số ghế."),
            new ScriptedChatReply($$"""{"Intent":"new_search","MovieNameText":"{{newTitle}}"}"""),
            new ScriptedChatReply("Mình bắt đầu lại tìm kiếm phim này cho bạn."));
        var conversationId = (await service.CreateAsync(userId, default)).Id;

        var first = await service.SendMessageAsync(userId, conversationId,
            new SendDiscoveryMessageRequest(Guid.NewGuid(), 1, $"Xem phim ở {seed.CinemaName} dưới 150 nghìn đủ 3 ghế."), default);
        Assert.Equal(2, first.Version);
        Assert.Equal(150_000m, first.AppliedFilters.MaxPrice);
        Assert.Equal(3, first.AppliedFilters.MinSeats);
        Assert.Equal(seed.CinemaName, first.AppliedFilters.CinemaName);

        var second = await service.SendMessageAsync(userId, conversationId,
            new SendDiscoveryMessageRequest(Guid.NewGuid(), 2, $"Thôi, tìm phim {newTitle} cho mình."), default);
        Assert.Equal(3, second.Version);
        Assert.Equal(newTitle, second.AppliedFilters.MovieNameText);
        Assert.Null(second.AppliedFilters.MaxPrice);
        Assert.Null(second.AppliedFilters.MinSeats);
        Assert.Null(second.AppliedFilters.CinemaName);
        Assert.Null(second.AppliedFilters.City);
        Assert.Empty(second.AppliedFilters.GenreSlugs);
        Assert.Null(second.AppliedFilters.DateFrom);
        Assert.Null(second.AppliedFilters.DateTo);

        // With no explicit dates the fresh search covers only the next seven zone days:
        // the showtime tomorrow survives while the one eight days out is dropped.
        var movie = Assert.Single(second.Movies, m => m.MovieId != seed.MovieId && m.Title == newTitle);
        var showtime = Assert.Single(movie.Showtimes);
        Assert.Equal(nearShowtimeId, showtime.ShowtimeId);

        await using var verify = fixture.CreateDbContext();
        var persisted = (await DiscoveryTestServices.CreateConversationService(verify).GetAsync(userId, conversationId, default))!;
        Assert.Equal(newTitle, persisted.Filters.MovieNameText);
        Assert.Null(persisted.Filters.MaxPrice);
        Assert.Null(persisted.Filters.MinSeats);
        Assert.Null(persisted.Filters.CinemaName);
    }

    [Fact]
    public async Task Relative_dates_and_past_midnight_windows_use_zone_time()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var db = fixture.CreateDbContext();
        var userId = await CreateUserAsync(db, $"discovery-scenario-zone-{Guid.NewGuid():N}@lumicine.test");
        var seed = await SeedCatalogAsync(db, suffix, new DateTime(2026, 11, 14, 16, 30, 0, DateTimeKind.Utc), 100_000m, 120_000m, 150_000m);
        // 16:30Z on the 14th is 23:30 UTC+7 (inside the late-night window); 03:00Z on the 15th is
        // 10:00 UTC+7 the next zone day (same date range, outside the window).
        var morningShowtimeId = await SeedExtraShowtimeAsync(
            db, seed.MovieId, seed.RoomId, new DateTime(2026, 11, 15, 3, 0, 0, DateTimeKind.Utc), "B", 100_000m);
        var (genreId, genreSlug) = await SeedGenreAsync(db, suffix);
        await AttachGenreAsync(db, seed.MovieId, genreId);
        var service = CreateService(db,
            new ScriptedChatReply($$"""{"Intent":"adjust","DateFrom":"2026-11-14","DateTo":"2026-11-15","GenreSlugs":["{{genreSlug}}"],"MovieNameText":"Thời Gian Vô Hình {{suffix}}"}"""),
            new ScriptedChatReply("Đã lưu khoảng cuối tuần cho bạn."),
            new ScriptedChatReply("""{"Intent":"adjust","TimeWindowStartMinutes":1320,"TimeWindowEndMinutes":120}"""),
            new ScriptedChatReply("Đã giới hạn khung giờ khuya cho bạn."));
        var conversationId = (await service.CreateAsync(userId, default)).Id;

        var first = await service.SendMessageAsync(userId, conversationId,
            new SendDiscoveryMessageRequest(Guid.NewGuid(), 1, $"Cho mình xem {suffix} cuối tuần này."), default);
        Assert.Equal(2, first.Version);
        Assert.Equal(new DateOnly(2026, 11, 14), first.AppliedFilters.DateFrom);
        Assert.Equal(new DateOnly(2026, 11, 15), first.AppliedFilters.DateTo);
        var weekendMovie = Assert.Single(first.Movies, m => m.MovieId == seed.MovieId);
        Assert.Equal(2, weekendMovie.Showtimes.Count);

        var firstSnapshot = (await service.GetAsync(userId, conversationId, default))!;
        Assert.Equal(new DateOnly(2026, 11, 14), firstSnapshot.Filters.DateFrom);
        Assert.Equal(new DateOnly(2026, 11, 15), firstSnapshot.Filters.DateTo);

        var second = await service.SendMessageAsync(userId, conversationId,
            new SendDiscoveryMessageRequest(Guid.NewGuid(), 2, "Chỉ khuya sau 22 giờ thôi nhé."), default);
        Assert.Equal(3, second.Version);
        Assert.Equal(1320, second.AppliedFilters.TimeWindowStartMinutes);
        Assert.Equal(120, second.AppliedFilters.TimeWindowEndMinutes);
        Assert.Equal([genreSlug], second.AppliedFilters.GenreSlugs);
        Assert.Equal(new DateOnly(2026, 11, 14), second.AppliedFilters.DateFrom);
        Assert.Equal(new DateOnly(2026, 11, 15), second.AppliedFilters.DateTo);

        // The window 22:00..02:00 wraps past midnight: the 23:30 UTC+7 showtime stays, the 10:00
        // UTC+7 showtime on the same date range is excluded.
        var lateMovie = Assert.Single(second.Movies, m => m.MovieId == seed.MovieId);
        var kept = Assert.Single(lateMovie.Showtimes);
        Assert.Equal(seed.ShowtimeId, kept.ShowtimeId);
        Assert.DoesNotContain(lateMovie.Showtimes, st => st.ShowtimeId == morningShowtimeId);

        var secondSnapshot = (await service.GetAsync(userId, conversationId, default))!;
        Assert.Equal(1320, secondSnapshot.Filters.TimeWindowStartMinutes);
        Assert.Equal(120, secondSnapshot.Filters.TimeWindowEndMinutes);
        Assert.Equal(new DateOnly(2026, 11, 14), secondSnapshot.Filters.DateFrom);
        Assert.Equal(new DateOnly(2026, 11, 15), secondSnapshot.Filters.DateTo);
    }

    [Fact]
    public async Task Turn_limit_stops_the_conversation_at_twenty_turns()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var db = fixture.CreateDbContext();
        var userId = await CreateUserAsync(db, $"discovery-scenario-limit-{Guid.NewGuid():N}@lumicine.test");
        await SeedCatalogAsync(db, suffix, FixedNow.AddHours(20), 100_000m);

        var replies = new List<ScriptedChatReply>();
        for (var turn = 1; turn <= 20; turn++)
        {
            replies.Add(new ScriptedChatReply($$"""{"Intent":"adjust","MovieNameText":"Phim lượt {{turn}}"}"""));
            replies.Add(new ScriptedChatReply($"Đã ghi nhận lượt {turn} của bạn."));
        }
        var service = CreateService(db, [.. replies]);
        var conversationId = (await service.CreateAsync(userId, default)).Id;

        for (var turn = 1; turn <= 20; turn++)
        {
            var response = await service.SendMessageAsync(userId, conversationId,
                new SendDiscoveryMessageRequest(Guid.NewGuid(), turn, $"Lượt thứ {turn}: cho mình gợi ý phim nhé."), default);
            Assert.Equal(turn, response.TurnNumber);
            Assert.Equal(turn + 1, response.Version);
        }

        var blocked = await Assert.ThrowsAsync<ApiException>(() => service.SendMessageAsync(userId, conversationId,
            new SendDiscoveryMessageRequest(Guid.NewGuid(), 21, "Lượt thứ 21 rồi, còn nhận không?"), default));
        Assert.Equal(409, blocked.StatusCode);

        await using var verify = fixture.CreateDbContext();
        Assert.Equal(40, await verify.DiscoveryMessages.CountAsync(m => m.ConversationId == conversationId));
        var persisted = (await DiscoveryTestServices.CreateConversationService(verify).GetAsync(userId, conversationId, default))!;
        Assert.Equal(20, persisted.TurnCount);
        Assert.Equal(21, persisted.Version);
    }

    [Fact]
    public async Task Clarification_then_adjust_flow_asks_then_persists_choice()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var db = fixture.CreateDbContext();
        var userId = await CreateUserAsync(db, $"discovery-scenario-clarify-{Guid.NewGuid():N}@lumicine.test");
        var seed = await SeedCatalogAsync(db, suffix, FixedNow.AddHours(20), 100_000m, 120_000m, 150_000m);
        var unknownCinema = $"Rạp Bóng Ma {suffix}";
        var service = CreateService(db,
            new ScriptedChatReply($$"""{"Intent":"adjust","CinemaName":"{{unknownCinema}}"}"""),
            new ScriptedChatReply($$"""{"Intent":"adjust","CinemaName":"{{seed.CinemaName}}"}"""),
            new ScriptedChatReply("Mình đã chuyển tới rạp bạn chọn."));
        var conversationId = (await service.CreateAsync(userId, default)).Id;

        var asked = await service.SendMessageAsync(userId, conversationId,
            new SendDiscoveryMessageRequest(Guid.NewGuid(), 1, $"Cho mình xem phim ở {unknownCinema}."), default);
        Assert.Equal(DiscoveryModes.Clarification, asked.Mode);
        Assert.Contains("chưa tìm thấy rạp", asked.Answer);
        Assert.Empty(asked.Movies);
        Assert.Null(asked.AppliedFilters.CinemaName);
        Assert.Null(asked.AppliedFilters.MaxPrice);
        var afterAsk = (await service.GetAsync(userId, conversationId, default))!;
        Assert.Null(afterAsk.Filters.CinemaName);

        var resolved = await service.SendMessageAsync(userId, conversationId,
            new SendDiscoveryMessageRequest(Guid.NewGuid(), 2, $"Ở {seed.CinemaName} thì có phim gì không?"), default);
        Assert.NotEqual(DiscoveryModes.Clarification, resolved.Mode);
        Assert.Equal(seed.CinemaName, resolved.AppliedFilters.CinemaName);
        Assert.NotEmpty(resolved.Movies.Where(m => m.MovieId == seed.MovieId));

        await using var verify = fixture.CreateDbContext();
        var persisted = (await DiscoveryTestServices.CreateConversationService(verify).GetAsync(userId, conversationId, default))!;
        Assert.Equal(3, persisted.Version);
        Assert.Equal(2, persisted.TurnCount);
        Assert.Equal(seed.CinemaName, persisted.Filters.CinemaName);
    }

    [Fact]
    public async Task Expiry_blocks_sends_but_keeps_history_readable()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var db = fixture.CreateDbContext();
        var userId = await CreateUserAsync(db, $"discovery-scenario-expiry-{Guid.NewGuid():N}@lumicine.test");
        var conversation = new DiscoveryConversation(Guid.NewGuid(), userId, "{}", DateTime.UtcNow.AddDays(-8));
        db.DiscoveryConversations.Add(conversation);
        db.DiscoveryMessages.Add(new DiscoveryMessage(Guid.NewGuid(), conversation.Id, 1,
            DiscoveryMessageRole.User, $"Cho mình phim {suffix} đi.", DateTime.UtcNow.AddDays(-8), requestId: Guid.NewGuid()));
        db.DiscoveryMessages.Add(new DiscoveryMessage(Guid.NewGuid(), conversation.Id, 1,
            DiscoveryMessageRole.Assistant, "Mình có vài gợi ý cho bạn.", DateTime.UtcNow.AddDays(-8)));
        await db.SaveChangesAsync();

        var service = DiscoveryTestServices.CreateConversationService(db);
        var details = await service.GetAsync(userId, conversation.Id, default);
        Assert.NotNull(details);
        Assert.True(details.IsExpired);
        Assert.Equal(2, details.Messages.Count);
        Assert.NotNull(details.Filters);

        var blocked = await Assert.ThrowsAsync<ApiException>(() => service.SendMessageAsync(userId, conversation.Id,
            new SendDiscoveryMessageRequest(Guid.NewGuid(), 1, "Còn suất nào không?"), default));
        Assert.Equal(410, blocked.StatusCode);

        await using var verify = fixture.CreateDbContext();
        Assert.Equal(2, await verify.DiscoveryMessages.CountAsync(m => m.ConversationId == conversation.Id));
        Assert.Equal(0, verify.DiscoveryConversations.Count(c => c.Id == conversation.Id && c.TurnCount > 0));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
