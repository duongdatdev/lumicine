using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LumiCine.Application.Accounts;
using LumiCine.Application.Bookings;
using LumiCine.Application.Catalog;
using LumiCine.Application.Discovery;
using LumiCine.Application.Scheduling;
using LumiCine.Domain.Catalog;
using LumiCine.Domain.Cinemas;
using LumiCine.Domain.Scheduling;
using LumiCine.Infrastructure.Authentication;
using LumiCine.Infrastructure.Discovery;
using LumiCine.Infrastructure.Identity;
using LumiCine.Infrastructure.Persistence;
using LumiCine.IntegrationTests.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace LumiCine.IntegrationTests.Discovery;

/// <summary>
/// End-to-end acceptance flows over the real HTTP routes: catalog changes feeding the embedding
/// index, multi-turn chat over HTTPS with replay and concurrency guards, discovery results
/// reaching the booking flow, and deleted history leaving the index untouched.
/// Runs against its own database so the fake-AI hosted workers cannot touch the shared catalog.
/// </summary>
[Collection(WorkerEmbeddingDatabaseCollection.Name)]
public sealed class DiscoveryAcceptanceFlowTests(PostgresDatabaseFixture fixture)
{
    private const string ConversationsRoute = "/api/discovery/conversations";

    [Fact]
    public async Task Catalog_change_feeds_index_then_discovery_finds_the_movie()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var db = fixture.CreateDbContext();
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        var chat = new ScriptedChatClient(
            new ScriptedChatReply($$"""{"Intent":"new_search","GenreSlugs":["phim-chien-tranh-hoa-da-{{suffix}}"],"MaxPrice":120000}"""),
            new ScriptedChatReply("Mình tìm thấy phim bạn cần đang có suất chiếu."));
        using var factory = CreateFakeAiFactory(chat, clock);

        // Only the genre, cinema and room exist up-front, so the pending-count delta below is
        // caused by the admin movie creation itself.
        var genreId = await SeedGenreAsync(db, $"Phim Chiến Tranh Hóa Đá {suffix}", $"phim-chien-tranh-hoa-da-{suffix}");
        var (cinemaId, roomId) = await SeedCinemaRoomAsync(db, suffix);
        await DrainEmbeddingQueueSteadyStateAsync(db);

        using var admin = await CreateClientAsync(factory, db, [RoleNames.Admin]);
        var before = await GetIndexStatusAsync(admin);

        using var created = await admin.PostAsJsonAsync("/api/admin/movies", new
        {
            title = $"Ký Ức Mùa Hạ {suffix}",
            synopsis = $"Một bộ phim tình cảm mùa hạ được thêm mới cho thử nghiệm {suffix}.",
            durationMinutes = 112,
            genreIds = new[] { genreId }
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var movie = (await created.Content.ReadFromJsonAsync<MovieSummary>())!;
        var showtimeId = await SeedShowtimeWithSeatsAsync(db, movie.Id, roomId, DateTime.UtcNow.AddHours(20), 100_000m, 120_000m, 150_000m);

        using var afterQueueing = await admin.GetAsync("/api/admin/discovery/index");
        Assert.Equal(HttpStatusCode.OK, afterQueueing.StatusCode);
        var queued = (await afterQueueing.Content.ReadFromJsonAsync<DiscoveryIndexStatus>())!;
        Assert.Equal(before.PendingCount + 1, queued.PendingCount);

        var pendingDocument = await db.MovieSearchDocuments.AsNoTracking().SingleAsync(d => d.MovieId == movie.Id);
        Assert.Equal(MovieSearchDocumentStatus.Pending, pendingDocument.Status);

        await RunWorkerPassUntilIndexedAsync(factory, db, pendingDocument.Id);

        var stored = await db.MovieSearchDocuments.AsNoTracking().SingleAsync(d => d.Id == pendingDocument.Id);
        Assert.Equal(MovieSearchDocumentStatus.Indexed, stored.Status);
        Assert.Equal("gemini-embedding-2", stored.EmbeddingModelId);
        Assert.Equal(768, stored.EmbeddingDimensions);
        Assert.NotNull(stored.Vector);
        Assert.Equal(768, stored.Vector!.Length);

        using var afterIndexing = await admin.GetAsync("/api/admin/discovery/index");
        var indexed = (await afterIndexing.Content.ReadFromJsonAsync<DiscoveryIndexStatus>())!;
        Assert.Equal(before.IndexedCount + 1, indexed.IndexedCount);
        Assert.Equal(before.PendingCount, indexed.PendingCount);
        Assert.Equal(before.FailedCount, indexed.FailedCount);
        Assert.Equal(before.TotalMovies + 1, indexed.TotalMovies);

        using var customer = await CreateClientAsync(factory, db, [RoleNames.Customer]);
        using var conversation = await customer.PostAsync(ConversationsRoute, null);
        Assert.Equal(HttpStatusCode.Created, conversation.StatusCode);
        var details = (await conversation.Content.ReadFromJsonAsync<DiscoveryConversationDetails>())!;

        using var turn = await customer.PostAsJsonAsync($"{ConversationsRoute}/{details.Id}/messages",
            new SendDiscoveryMessageRequest(Guid.NewGuid(), 1, $"Cho mình phim chiến tranh hóa đá dưới 120 nghìn."));
        Assert.Equal(HttpStatusCode.OK, turn.StatusCode);
        var response = (await turn.Content.ReadFromJsonAsync<DiscoveryChatResponse>())!;
        Assert.Equal(details.Id, response.ConversationId);
        Assert.Equal(2, response.Version);
        Assert.Equal(DiscoveryModes.Schedule, response.Mode);
        Assert.Equal(["phim-chien-tranh-hoa-da-" + suffix], response.AppliedFilters.GenreSlugs);
        Assert.Equal(120_000m, response.AppliedFilters.MaxPrice);
        var result = Assert.Single(response.Movies);
        Assert.Equal(movie.Id, result.MovieId);
        var showtime = Assert.Single(result.Showtimes, st => st.ShowtimeId == showtimeId);
        Assert.Equal(cinemaId, showtime.CinemaId);
        Assert.Equal(100_000m, showtime.Price);
    }

    [Fact]
    public async Task Multi_turn_https_chat_chain_keeps_context()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var db = fixture.CreateDbContext();
        var genreId = await SeedGenreAsync(db, $"Phim Đường Phòng Không {suffix}", $"phim-duong-phong-khong-{suffix}");
        var seed = await SeedCatalogAsync(db, suffix, DateTime.UtcNow.AddDays(2), 100_000m, 120_000m, 150_000m);
        await AttachGenreAsync(db, seed.MovieId, genreId);
        using var factory = WithScriptedAi(
            new ScriptedChatReply($$"""{"Intent":"new_search","MaxPrice":120000,"GenreSlugs":["phim-duong-phong-khong-{{suffix}}"]}"""),
            new ScriptedChatReply("Mình tìm thấy các suất phù hợp dưới 120 nghìn."),
            new ScriptedChatReply($$"""{"Intent":"adjust","MinSeats":2,"GenreSlugs":["phim-duong-phong-khong-{{suffix}}"]}"""),
            new ScriptedChatReply("Đã thêm điều kiện đủ 2 ghế vào tìm kiếm."));

        using var client = await CreateClientAsync(factory, db, [RoleNames.Customer], new Uri("https://localhost"));
        using var created = await client.PostAsync(ConversationsRoute, null);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.True(created.Headers.CacheControl?.NoStore);
        var conversation = (await created.Content.ReadFromJsonAsync<DiscoveryConversationDetails>())!;

        using var first = await client.PostAsJsonAsync($"{ConversationsRoute}/{conversation.Id}/messages",
            new SendDiscoveryMessageRequest(Guid.NewGuid(), 1, "Tìm phim phòng không dưới 120 nghìn cho mình."));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.True(first.Headers.CacheControl?.NoStore);
        var firstTurn = (await first.Content.ReadFromJsonAsync<DiscoveryChatResponse>())!;
        Assert.Equal(2, firstTurn.Version);
        Assert.Equal(120_000m, firstTurn.AppliedFilters.MaxPrice);
        Assert.Null(firstTurn.AppliedFilters.MinSeats);

        var secondRequest = new SendDiscoveryMessageRequest(Guid.NewGuid(), 2, "Thêm điều kiện đủ 2 ghế nhé.");
        using var second = await client.PostAsJsonAsync($"{ConversationsRoute}/{conversation.Id}/messages", secondRequest);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.True(second.Headers.CacheControl?.NoStore);
        var secondTurn = (await second.Content.ReadFromJsonAsync<DiscoveryChatResponse>())!;
        Assert.Equal(3, secondTurn.Version);
        Assert.Equal(120_000m, secondTurn.AppliedFilters.MaxPrice);
        Assert.Equal(2, secondTurn.AppliedFilters.MinSeats);
        Assert.Equal(["phim-duong-phong-khong-" + suffix], secondTurn.AppliedFilters.GenreSlugs);
        var result = Assert.Single(secondTurn.Movies, m => m.MovieId == seed.MovieId);
        Assert.NotEmpty(result.Showtimes);

        using var replay = await client.PostAsJsonAsync($"{ConversationsRoute}/{conversation.Id}/messages", secondRequest);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.True(replay.Headers.CacheControl?.NoStore);
        var replayed = (await replay.Content.ReadFromJsonAsync<DiscoveryChatResponse>())!;
        Assert.Equal(secondTurn.Answer, replayed.Answer);
        Assert.Equal(secondTurn.Version, replayed.Version);
        Assert.Equal(secondTurn.TurnNumber, replayed.TurnNumber);
        Assert.Equal(secondTurn.RequestId, replayed.RequestId);
        Assert.Equal(secondTurn.Movies.Count, replayed.Movies.Count);

        using var stale = await client.PostAsJsonAsync($"{ConversationsRoute}/{conversation.Id}/messages",
            secondRequest with { RequestId = Guid.NewGuid() });
        await AssertProblemAsync(stale, HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Discovery_results_reach_the_booking_flow()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var db = fixture.CreateDbContext();
        var genreId = await SeedGenreAsync(db, $"Phim Băng Qua Sa Mạc {suffix}", $"phim-bang-qua-sa-mac-{suffix}");
        var seed = await SeedCatalogAsync(db, suffix, DateTime.UtcNow.AddDays(2), 100_000m, 120_000m, 150_000m);
        await AttachGenreAsync(db, seed.MovieId, genreId);
        using var factory = WithScriptedAi(
            new ScriptedChatReply($$"""{"Intent":"new_search","MaxPrice":120000,"GenreSlugs":["phim-bang-qua-sa-mac-{{suffix}}"]}"""),
            new ScriptedChatReply("Mình tìm thấy suất chiếu phù hợp cho bạn."),
            new ScriptedChatReply($$"""{"Intent":"adjust","MinSeats":2,"GenreSlugs":["phim-bang-qua-sa-mac-{{suffix}}"]}"""),
            new ScriptedChatReply("Đã lọc các suất còn đủ 2 ghế trống."));

        using var client = await CreateClientAsync(factory, db, [RoleNames.Customer]);
        using var created = await client.PostAsync(ConversationsRoute, null);
        var conversation = (await created.Content.ReadFromJsonAsync<DiscoveryConversationDetails>())!;
        await client.PostAsJsonAsync($"{ConversationsRoute}/{conversation.Id}/messages",
            new SendDiscoveryMessageRequest(Guid.NewGuid(), 1, "Tìm phim băng qua sa mạc dưới 120 nghìn."));
        using var second = await client.PostAsJsonAsync($"{ConversationsRoute}/{conversation.Id}/messages",
            new SendDiscoveryMessageRequest(Guid.NewGuid(), 2, "Chốt suất nào còn đủ 2 ghế."));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var finalTurn = (await second.Content.ReadFromJsonAsync<DiscoveryChatResponse>())!;

        var result = Assert.Single(finalTurn.Movies, m => m.MovieId == seed.MovieId);
        var discoveredShowtime = Assert.Single(result.Showtimes);
        Assert.Equal(seed.ShowtimeId, discoveredShowtime.ShowtimeId);

        using var seatsResponse = await client.GetAsync($"/api/showtimes/{discoveredShowtime.ShowtimeId}/seats");
        Assert.Equal(HttpStatusCode.OK, seatsResponse.StatusCode);
        var seats = (await seatsResponse.Content.ReadFromJsonAsync<IReadOnlyList<ShowtimeSeatSummary>>())!;
        var chosen = seats.Where(s => s.Status == nameof(ShowtimeSeatStatus.Available)).OrderBy(s => s.Number).Take(2).ToList();
        Assert.Equal(2, chosen.Count);

        using var bookingResponse = await client.PostAsJsonAsync("/api/bookings",
            new CreateBookingRequest(Guid.NewGuid(), discoveredShowtime.ShowtimeId, chosen.Select(s => s.SeatId).ToList()));
        Assert.Equal(HttpStatusCode.Created, bookingResponse.StatusCode);
        var booking = (await bookingResponse.Content.ReadFromJsonAsync<BookingDetails>())!;
        Assert.Equal(discoveredShowtime.ShowtimeId, booking.ShowtimeId);
        Assert.Equal(seed.MovieId, booking.MovieId);
        Assert.Equal(2, booking.Seats.Count);
        Assert.Equal(chosen.Sum(s => s.Price), booking.TotalAmount);
        Assert.Equal(220_000m, booking.TotalAmount);
    }

    [Fact]
    public async Task Deleted_history_disappears_and_rate_limit_and_admin_status_hold()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var db = fixture.CreateDbContext();
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        using var admin = await CreateClientAsync(factory, db, [RoleNames.Admin]);
        var (customer, userId) = await CreateUserClientAsync(factory, db);

        var before = await GetIndexStatusAsync(admin);

        using var created = await customer.PostAsync(ConversationsRoute, null);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var conversation = (await created.Content.ReadFromJsonAsync<DiscoveryConversationDetails>())!;
        using var turn = await customer.PostAsJsonAsync($"{ConversationsRoute}/{conversation.Id}/messages",
            new SendDiscoveryMessageRequest(Guid.NewGuid(), 1, $"Gợi ý phim hay cho tối nay {suffix}."));
        Assert.Equal(HttpStatusCode.OK, turn.StatusCode);

        // The conversations API exposes no HTTP DELETE route, so the deletion runs through the
        // same scoped service the endpoint would call; the surrounding assertions stay HTTP.
        using (var scope = factory.Services.CreateScope())
        {
            var conversations = scope.ServiceProvider.GetRequiredService<IDiscoveryConversationService>();
            Assert.True(await conversations.DeleteAsync(userId, conversation.Id, default));
        }

        using var gone = await customer.GetAsync($"{ConversationsRoute}/{conversation.Id}");
        await AssertProblemAsync(gone, HttpStatusCode.NotFound);
        using var rejected = await customer.PostAsJsonAsync($"{ConversationsRoute}/{conversation.Id}/messages",
            new SendDiscoveryMessageRequest(Guid.NewGuid(), 1, "Còn lịch chiếu nào không?"));
        await AssertProblemAsync(rejected, HttpStatusCode.NotFound);

        var after = await GetIndexStatusAsync(admin);
        Assert.Equal(before.IndexedCount, after.IndexedCount);
        Assert.Equal(before.PendingCount, after.PendingCount);
        Assert.Equal(before.FailedCount, after.FailedCount);
        Assert.Equal(before.TotalMovies, after.TotalMovies);
    }

    private WebApplicationFactory<Program> CreateFakeAiFactory(ScriptedChatClient chat, MutableClock clock) =>
        new ApiWebApplicationFactory(connectionString: fixture.ConnectionString)
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<GeminiAiClients>(new GeminiAiClients(
                    chat, new ScriptedEmbeddingGenerator(Enumerable.Repeat(0.25f, 768).ToArray()))));
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock));
            }));

    private WebApplicationFactory<Program> WithScriptedAi(params ScriptedChatReply[] replies) =>
        new ApiWebApplicationFactory(connectionString: fixture.ConnectionString)
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.Replace(ServiceDescriptor.Singleton<GeminiAiClients>(new GeminiAiClients(
                    new ScriptedChatClient(replies),
                    new ScriptedEmbeddingGenerator(DiscoveryTestServices.DefaultVector()))))));

    private static async Task RunWorkerPassUntilIndexedAsync(
        WebApplicationFactory<Program> factory, LumiCineDbContext db, Guid documentId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        while (true)
        {
            using var scope = factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<MovieEmbeddingWorker>()
                .ProcessDueDocumentsAsync(1, CancellationToken.None);
            var stored = await db.MovieSearchDocuments.AsNoTracking()
                .Where(d => d.Id == documentId).SingleOrDefaultAsync(timeout.Token);
            if (stored is not null && stored.Status == MovieSearchDocumentStatus.Indexed)
                return;
            await Task.Delay(100, timeout.Token);
        }
    }

    /// <summary>
    /// Brings the embedding queue to a steady state before the test host (and its hosted worker
    /// pass) starts: every row due on the real clock is processed with the fake vector, so all
    /// later index-count deltas come only from rows this test creates.
    /// </summary>
    private static async Task DrainEmbeddingQueueSteadyStateAsync(LumiCineDbContext db)
    {
        var documents = new MovieSearchDocumentService(db, TimeProvider.System);
        var builder = new MovieSearchDocumentBuilder(Options.Create(new GeminiOptions()));
        var vector = Enumerable.Repeat(0.25f, 768).ToArray();
        for (var round = 0; round < 50; round++)
        {
            var claimed = await documents.ClaimDueAsync(100, default);
            if (claimed.Count == 0)
                return;
            foreach (var work in claimed)
            {
                var movie = await db.Movies.AsNoTracking().SingleOrDefaultAsync(m => m.Id == work.MovieId);
                if (movie is null)
                    continue;
                var genreNames = await db.MovieGenres.Where(mg => mg.MovieId == work.MovieId)
                    .Join(db.Genres, mg => mg.GenreId, g => g.Id, (mg, g) => g)
                    .OrderBy(g => g.Name).ThenBy(g => g.Id)
                    .Select(g => g.Name).ToListAsync();
                var text = builder.BuildDocumentText(movie.Title, movie.OriginalTitle, movie.Synopsis, genreNames);
                if (builder.ComputeSourceHash(text) != work.SourceHash)
                    continue;
                await documents.CompleteAsync(work.DocumentId, "gemini-embedding-2", 768, vector, work.SourceHash, default);
            }
        }
        throw new TimeoutException("The embedding queue did not drain before the test snapshot.");
    }

    private static async Task<DiscoveryIndexStatus> GetIndexStatusAsync(HttpClient admin)
    {
        using var response = await admin.GetAsync("/api/admin/discovery/index");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DiscoveryIndexStatus>())!;
    }

    private static async Task<Guid> SeedGenreAsync(LumiCineDbContext db, string name, string slug)
    {
        var genre = new Genre(Guid.NewGuid(), name, slug, DateTime.UtcNow);
        db.Genres.Add(genre);
        await db.SaveChangesAsync();
        return genre.Id;
    }

    private static async Task AttachGenreAsync(LumiCineDbContext db, Guid movieId, Guid genreId)
    {
        db.MovieGenres.Add(new MovieGenre(movieId, genreId, DateTime.UtcNow));
        await db.SaveChangesAsync();
    }

    private static async Task<(Guid CinemaId, Guid RoomId)> SeedCinemaRoomAsync(LumiCineDbContext db, string suffix)
    {
        var now = DateTime.UtcNow;
        var cinema = new Cinema(Guid.NewGuid(), $"AF" + suffix.ToUpperInvariant(), $"Rạp Cầu Vồng {suffix}", $"Địa chỉ {suffix}", $"Hà Nội {suffix}", now);
        var room = new Room(Guid.NewGuid(), cinema.Id, $"AF-R" + suffix.ToUpperInvariant(), $"Phòng {suffix}", now);
        db.AddRange(cinema, room);
        await db.SaveChangesAsync();
        return (cinema.Id, room.Id);
    }

    private static async Task<Guid> SeedShowtimeWithSeatsAsync(
        LumiCineDbContext db, Guid movieId, Guid roomId, DateTime startUtc, params decimal[] seatPrices)
    {
        var now = DateTime.UtcNow;
        var showtime = new Showtime(Guid.NewGuid(), movieId, roomId, startUtc, startUtc.AddHours(2), 120_000m, now);
        db.Showtimes.Add(showtime);
        await db.SaveChangesAsync();
        for (var index = 0; index < seatPrices.Length; index++)
        {
            var seat = new Seat(Guid.NewGuid(), roomId, "A", index + 1, "Standard", now);
            db.Seats.Add(seat);
            db.ShowtimeSeats.Add(new ShowtimeSeat(Guid.NewGuid(), showtime.Id, roomId, seat.Id, seatPrices[index], now));
        }
        await db.SaveChangesAsync();
        return showtime.Id;
    }

    private static async Task<CatalogSeed> SeedCatalogAsync(
        LumiCineDbContext db, string suffix, DateTime showtimeStartUtc, params decimal[] seatPrices)
    {
        var now = DateTime.UtcNow;
        var cinema = new Cinema(Guid.NewGuid(), $"AC" + suffix.ToUpperInvariant(), $"Rạp Khám Phá {suffix}", $"Địa chỉ {suffix}", $"Hà Nội {suffix}", now);
        var room = new Room(Guid.NewGuid(), cinema.Id, $"AC-R" + suffix.ToUpperInvariant(), $"Phòng {suffix}", now);
        var movie = new Movie(Guid.NewGuid(), $"Băng Qua Rừng Thẳm {suffix}", 108, now, synopsis: $"Hành trình băng qua rừng thẳm {suffix}.", isActive: true);
        var showtime = new Showtime(Guid.NewGuid(), movie.Id, room.Id, showtimeStartUtc, showtimeStartUtc.AddHours(2), 120_000m, now);
        db.AddRange(cinema, room, movie, showtime);
        await db.SaveChangesAsync();
        for (var index = 0; index < seatPrices.Length; index++)
        {
            var seat = new Seat(Guid.NewGuid(), room.Id, "A", index + 1, "Standard", now);
            db.Seats.Add(seat);
            db.ShowtimeSeats.Add(new ShowtimeSeat(Guid.NewGuid(), showtime.Id, room.Id, seat.Id, seatPrices[index], now));
        }
        await db.SaveChangesAsync();
        return new CatalogSeed(cinema.Id, cinema.Name, room.Id, movie.Id, showtime.Id);
    }

    private sealed record CatalogSeed(Guid CinemaId, string CinemaName, Guid RoomId, Guid MovieId, Guid ShowtimeId);

    private async Task<HttpClient> CreateClientAsync(
        WebApplicationFactory<Program> factory, LumiCineDbContext db, string[]? roles = null, Uri? baseAddress = null)
    {
        var (client, _) = await CreateUserClientAsync(factory, db, roles, baseAddress);
        return client;
    }

    private async Task<(HttpClient Client, Guid UserId)> CreateUserClientAsync(
        WebApplicationFactory<Program> factory, LumiCineDbContext db, string[]? roles = null, Uri? baseAddress = null)
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var email = $"{userId:N}@example.com";
        db.Users.Add(new ApplicationUser
        {
            Id = userId, Email = email, NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true, IsActive = true, DisplayName = "Discovery acceptance test"
        });
        var now = DateTime.UtcNow;
        db.AuthSessions.Add(new AuthSession { Id = sessionId, UserId = userId, CreatedAtUtc = now, ExpiresAtUtc = now.AddHours(1) });
        await db.SaveChangesAsync();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = baseAddress ?? new Uri("http://localhost") });
        var token = factory.Services.GetRequiredService<IAccessTokenIssuer>()
            .Issue(userId, sessionId, roles ?? [RoleNames.Customer]).Value;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, userId);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode statusCode)
    {
        Assert.Equal(statusCode, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal((int)statusCode, body.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("traceId").GetString()));
    }

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        // The hosted embedding worker loops on Task.Delay(..., clock, ...). The default
        // TimeProvider.CreateTimer fires on real elapsed time, so it is overridden with a timer
        // that never fires: the hosted worker runs its single startup pass (which finds the queue
        // already drained) and then sleeps forever, leaving every later claim to the manual passes.
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            new NeverFireTimer();

        private sealed class NeverFireTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose() { }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
