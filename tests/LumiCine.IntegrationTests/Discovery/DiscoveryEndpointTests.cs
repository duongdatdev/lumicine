using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LumiCine.Application.Accounts;
using LumiCine.Application.Common;
using LumiCine.Application.Discovery;
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LumiCine.IntegrationTests.Discovery;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class DiscoveryEndpointTests(PostgresDatabaseFixture fixture)
{
    private const string Route = "/api/discovery/conversations";

    [Fact]
    public async Task Anonymous_conversation_creation_is_unauthorized()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(Route, null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_returns_201_with_location_and_initial_state()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        using var client = await CreateClientAsync(factory);

        using var created = await client.PostAsync(Route, null);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.True(created.Headers.CacheControl?.NoStore);
        var details = (await created.Content.ReadFromJsonAsync<DiscoveryConversationDetails>())!;
        Assert.StartsWith($"{Route}/{details.Id}", created.Headers.Location!.PathAndQuery);
        Assert.Equal(1, details.Version);
        Assert.Equal(0, details.TurnCount);
        Assert.False(details.IsExpired);
        Assert.Empty(details.Messages);
        Assert.NotNull(details.Filters);
        Assert.True(details.CreatedAtUtc != default);
        Assert.True(details.ExpiresAtUtc > details.CreatedAtUtc);
    }

    [Fact]
    public async Task List_returns_only_own_conversations_paginated()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        using var mine = await CreateClientAsync(factory);
        using var other = await CreateClientAsync(factory);

        var ownIds = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            using var created = await mine.PostAsync(Route, null);
            ownIds.Add((await created.Content.ReadFromJsonAsync<DiscoveryConversationDetails>())!.Id);
        }
        using var foreign = await other.PostAsync(Route, null);
        var foreignId = (await foreign.Content.ReadFromJsonAsync<DiscoveryConversationDetails>())!.Id;

        using var firstPage = await mine.GetAsync($"{Route}?page=1&pageSize=2");
        Assert.Equal(HttpStatusCode.OK, firstPage.StatusCode);
        var list = (await firstPage.Content.ReadFromJsonAsync<PagedResult<DiscoveryConversationSummary>>())!;
        Assert.Equal(3, list.TotalCount);
        Assert.Equal(1, list.Page);
        Assert.Equal(2, list.PageSize);
        Assert.Equal(2, list.Items.Count);
        Assert.All(list.Items, item => Assert.Contains(item.Id, ownIds));
        Assert.DoesNotContain(list.Items, item => item.Id == foreignId);
        Assert.All(list.Items, item => Assert.False(item.IsExpired));

        using var secondPage = await mine.GetAsync($"{Route}?page=2&pageSize=2");
        var rest = (await secondPage.Content.ReadFromJsonAsync<PagedResult<DiscoveryConversationSummary>>())!;
        Assert.Equal(3, rest.TotalCount);
        Assert.Single(rest.Items, item => ownIds.Contains(item.Id));
    }

    [Fact]
    public async Task Get_returns_details_and_foreign_conversations_look_missing()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        using var client = await CreateClientAsync(factory);
        using var other = await CreateClientAsync(factory);
        using var created = await client.PostAsync(Route, null);
        var details = (await created.Content.ReadFromJsonAsync<DiscoveryConversationDetails>())!;

        using var own = await client.GetAsync($"{Route}/{details.Id}");
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.True(own.Headers.CacheControl?.NoStore);
        var fetched = (await own.Content.ReadFromJsonAsync<DiscoveryConversationDetails>())!;
        Assert.Equal(details.Id, fetched.Id);
        Assert.Equal(details.Version, fetched.Version);

        using var foreign = await other.GetAsync($"{Route}/{details.Id}");
        using var unknown = await client.GetAsync($"{Route}/{Guid.NewGuid()}");
        await AssertProblemAsync(foreign, HttpStatusCode.NotFound);
        await AssertProblemAsync(unknown, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Send_message_runs_an_ai_turn_and_returns_the_full_response()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var db = fixture.CreateDbContext();
        var (movieId, showtimeId) = await SeedCatalogAsync(db, suffix, 100_000m, 120_000m, 150_000m);
        var parserReply = $$"""{"Intent":"new_search","MovieNameText":"Thời Gian Vô Hình {{suffix}}"}""";
        const string writerReply = "Mình tìm thấy phim phù hợp cho bạn ngay đây.";
        using var baseFactory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        using var factory = WithScriptedAi(baseFactory, parserReply, writerReply);
        using var client = await CreateClientAsync(factory);
        using var created = await client.PostAsync(Route, null);
        var conversation = (await created.Content.ReadFromJsonAsync<DiscoveryConversationDetails>())!;

        var requestId = Guid.NewGuid();
        using var response = await client.PostAsJsonAsync($"{Route}/{conversation.Id}/messages",
            new SendDiscoveryMessageRequest(requestId, 1, $"Gợi ý phim Thời Gian Vô Hình {suffix} cho tôi."));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var turn = (await response.Content.ReadFromJsonAsync<DiscoveryChatResponse>())!;
        Assert.Equal(conversation.Id, turn.ConversationId);
        Assert.Equal(2, turn.Version);
        Assert.Equal(requestId, turn.RequestId);
        Assert.Equal(1, turn.TurnNumber);
        Assert.Equal(writerReply, turn.Answer);
        Assert.Equal(DiscoveryModes.Keyword, turn.Mode);
        Assert.False(turn.IsDegraded);
        Assert.Empty(turn.Warnings);
        Assert.Equal($"Thời Gian Vô Hình {suffix}", turn.AppliedFilters.MovieNameText);
        Assert.Null(turn.AppliedFilters.MaxPrice);
        Assert.Null(turn.AppliedFilters.CinemaName);
        Assert.Null(turn.AppliedFilters.City);
        Assert.Empty(turn.AppliedFilters.GenreSlugs);
        Assert.True(turn.GeneratedAtUtc != default);
        Assert.True(turn.AvailabilityAsOfUtc != default);
        var movie = Assert.Single(turn.Movies);
        Assert.Equal(movieId, movie.MovieId);
        Assert.Equal($"Thời Gian Vô Hình {suffix}", movie.Title);
        var showtime = Assert.Single(movie.Showtimes);
        Assert.Equal(showtimeId, showtime.ShowtimeId);
        Assert.Equal(100_000m, showtime.Price);
        Assert.Equal(3, showtime.AvailableSeats);
    }

    [Fact]
    public async Task Missing_or_empty_message_fields_return_validation_problems()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        using var client = await CreateClientAsync(factory);
        using var created = await client.PostAsync(Route, null);
        var conversation = (await created.Content.ReadFromJsonAsync<DiscoveryConversationDetails>())!;

        using var missing = await client.PostAsJsonAsync($"{Route}/{conversation.Id}/messages", new { });
        using var empty = await client.PostAsJsonAsync($"{Route}/{conversation.Id}/messages",
            new SendDiscoveryMessageRequest(Guid.NewGuid(), 1, "   "));

        await AssertProblemAsync(missing, HttpStatusCode.BadRequest);
        var missingBody = await missing.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(missingBody);
        Assert.True(document.RootElement.GetProperty("errors").EnumerateObject().Any());
        await AssertProblemAsync(empty, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Replay_returns_identical_answer_and_stale_version_conflicts()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var db = fixture.CreateDbContext();
        await SeedCatalogAsync(db, suffix, 100_000m);
        var parserReply = $$"""{"Intent":"new_search","MovieNameText":"Thời Gian Vô Hình {{suffix}}"}""";
        using var baseFactory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        using var factory = WithScriptedAi(baseFactory, parserReply, "Phim này đang có suất, bạn chọn giờ nhé.");
        using var client = await CreateClientAsync(factory);
        using var created = await client.PostAsync(Route, null);
        var conversation = (await created.Content.ReadFromJsonAsync<DiscoveryConversationDetails>())!;
        var request = new SendDiscoveryMessageRequest(Guid.NewGuid(), 1, $"Gợi ý phim Thời Gian Vô Hình {suffix}.");

        using var first = await client.PostAsJsonAsync($"{Route}/{conversation.Id}/messages", request);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var turn = (await first.Content.ReadFromJsonAsync<DiscoveryChatResponse>())!;

        using var replay = await client.PostAsJsonAsync($"{Route}/{conversation.Id}/messages", request);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var replayed = (await replay.Content.ReadFromJsonAsync<DiscoveryChatResponse>())!;
        Assert.Equal(turn.Answer, replayed.Answer);
        Assert.Equal(turn.Version, replayed.Version);
        Assert.Equal(turn.TurnNumber, replayed.TurnNumber);
        Assert.Equal(turn.RequestId, replayed.RequestId);

        using var stale = await client.PostAsJsonAsync($"{Route}/{conversation.Id}/messages",
            request with { RequestId = Guid.NewGuid() });
        await AssertProblemAsync(stale, HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Message_on_a_foreign_conversation_is_missing()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        using var owner = await CreateClientAsync(factory);
        using var stranger = await CreateClientAsync(factory);
        using var created = await owner.PostAsync(Route, null);
        var conversation = (await created.Content.ReadFromJsonAsync<DiscoveryConversationDetails>())!;

        using var response = await stranger.PostAsJsonAsync($"{Route}/{conversation.Id}/messages",
            new SendDiscoveryMessageRequest(Guid.NewGuid(), 1, "Gợi ý phim cho tôi."));

        await AssertProblemAsync(response, HttpStatusCode.NotFound);
    }

    private static WebApplicationFactory<Program> WithScriptedAi(ApiWebApplicationFactory factory,
        string parserReply, string writerReply) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Replace(ServiceDescriptor.Singleton<GeminiAiClients>(new GeminiAiClients(
                new ScriptedChatClient(new ScriptedChatReply(parserReply), new ScriptedChatReply(writerReply)),
                new ScriptedEmbeddingGenerator(DiscoveryTestServices.DefaultVector()))))));

    private static async Task<(Guid MovieId, Guid ShowtimeId)> SeedCatalogAsync(
        LumiCineDbContext db, string suffix, params decimal[] seatPrices)
    {
        var now = DateTime.UtcNow;
        var cinema = new Cinema(Guid.NewGuid(), $"DE" + suffix.ToUpperInvariant(), $"Rạp Trung Tâm {suffix}", $"Địa chỉ {suffix}", $"Hà Nội {suffix}", now);
        var room = new Room(Guid.NewGuid(), cinema.Id, $"DE-R" + suffix.ToUpperInvariant(), $"Phòng {suffix}", now);
        var movie = new Movie(Guid.NewGuid(), $"Thời Gian Vô Hình {suffix}", 110, now, isActive: true);
        var showtime = new Showtime(Guid.NewGuid(), movie.Id, room.Id, now.AddHours(20), now.AddHours(22), 120_000m, now);
        await db.AddRangeAsync(cinema, room, movie, showtime);
        await db.SaveChangesAsync();

        foreach (var (price, index) in seatPrices.Select((p, i) => (p, i)))
        {
            var seat = new Seat(Guid.NewGuid(), room.Id, "A", index + 1, "Standard", now);
            db.Seats.Add(seat);
            db.ShowtimeSeats.Add(new ShowtimeSeat(Guid.NewGuid(), showtime.Id, room.Id, seat.Id, price, now));
        }
        await db.SaveChangesAsync();
        return (movie.Id, showtime.Id);
    }

    [Fact]
    public async Task Delete_removes_conversation_and_returns_204_and_foreign_returns_404()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        using var client = await CreateClientAsync(factory);
        using var otherClient = await CreateClientAsync(factory);

        using var created = await client.PostAsync(Route, null);
        var details = (await created.Content.ReadFromJsonAsync<DiscoveryConversationDetails>())!;

        using var foreignDelete = await otherClient.DeleteAsync($"{Route}/{details.Id}");
        Assert.Equal(HttpStatusCode.NotFound, foreignDelete.StatusCode);

        using var ownDelete = await client.DeleteAsync($"{Route}/{details.Id}");
        Assert.Equal(HttpStatusCode.NoContent, ownDelete.StatusCode);

        using var getAfterDelete = await client.GetAsync($"{Route}/{details.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getAfterDelete.StatusCode);
    }

    private async Task<HttpClient> CreateClientAsync(WebApplicationFactory<Program> factory, string[]? roles = null)
    {
        await using var db = fixture.CreateDbContext();
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var email = $"{userId:N}@example.com";
        db.Users.Add(new ApplicationUser
        {
            Id = userId, Email = email, NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true, IsActive = true, DisplayName = "Discovery test"
        });
        var now = DateTime.UtcNow;
        db.AuthSessions.Add(new AuthSession { Id = sessionId, UserId = userId, CreatedAtUtc = now, ExpiresAtUtc = now.AddHours(1) });
        await db.SaveChangesAsync();
        var client = factory.CreateClient();
        var token = factory.Services.GetRequiredService<IAccessTokenIssuer>()
            .Issue(userId, sessionId, roles ?? ["Customer"]).Value;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode statusCode)
    {
        Assert.Equal(statusCode, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal((int)statusCode, body.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("traceId").GetString()));
    }
}
