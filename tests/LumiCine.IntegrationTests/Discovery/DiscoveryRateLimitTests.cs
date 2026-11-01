using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LumiCine.Application.Accounts;
using LumiCine.Application.Discovery;
using LumiCine.Infrastructure.Authentication;
using LumiCine.Infrastructure.Identity;
using LumiCine.IntegrationTests.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LumiCine.IntegrationTests.Discovery;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class DiscoveryRateLimitTests(PostgresDatabaseFixture fixture)
{
    private const string Route = "/api/discovery/conversations";

    [Fact]
    public async Task Third_request_in_the_window_is_rejected_per_user()
    {
        using var baseFactory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        using var factory = CreateLimitedFactory(baseFactory, permitLimit: 2);
        using var first = await CreateClientAsync(factory);
        using var second = await CreateClientAsync(factory);

        using var one = await first.GetAsync(Route);
        using var two = await first.GetAsync(Route);
        Assert.Equal(HttpStatusCode.OK, one.StatusCode);
        Assert.Equal(HttpStatusCode.OK, two.StatusCode);

        using var rejected = await first.GetAsync(Route);
        Assert.Equal((HttpStatusCode)429, rejected.StatusCode);
        Assert.Equal("application/problem+json", rejected.Content.Headers.ContentType?.MediaType);
        Assert.True(rejected.Headers.TryGetValues("Retry-After", out var retryAfter));
        Assert.True(int.Parse(Assert.Single(retryAfter!), CultureInfo.InvariantCulture) >= 1);
        using var body = await JsonDocument.ParseAsync(await rejected.Content.ReadAsStreamAsync());
        Assert.Equal(429, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Too many requests", body.RootElement.GetProperty("title").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("traceId").GetString()));

        // The partition follows the user id, so another account is never blocked by this quota.
        using var otherUser = await second.GetAsync(Route);
        Assert.Equal(HttpStatusCode.OK, otherUser.StatusCode);
    }

    [Fact]
    public async Task Quota_is_shared_between_message_and_list_endpoints()
    {
        using var baseFactory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        using var factory = CreateLimitedFactory(baseFactory, permitLimit: 2);
        using var client = await CreateClientAsync(factory);
        using var created = await client.PostAsync(Route, null);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var conversation = (await created.Content.ReadFromJsonAsync<DiscoveryConversationDetails>())!;

        using var message = await client.PostAsJsonAsync($"{Route}/{conversation.Id}/messages",
            new SendDiscoveryMessageRequest(Guid.NewGuid(), 1, "Gợi ý phim cho tôi."));
        Assert.Equal(HttpStatusCode.OK, message.StatusCode);

        // One quota covers every discovery action: the next turn and the list both reject.
        using var limitedMessage = await client.PostAsJsonAsync($"{Route}/{conversation.Id}/messages",
            new SendDiscoveryMessageRequest(Guid.NewGuid(), 2, "Gợi ý phim khác cho tôi."));
        using var limitedList = await client.GetAsync(Route);
        Assert.Equal((HttpStatusCode)429, limitedMessage.StatusCode);
        Assert.Equal((HttpStatusCode)429, limitedList.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateLimitedFactory(
        ApiWebApplicationFactory baseFactory, int permitLimit) =>
        baseFactory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ai:DiscoveryRateLimit:PermitLimit"] = permitLimit.ToString(CultureInfo.InvariantCulture),
                ["Ai:DiscoveryRateLimit:WindowSeconds"] = "60"
            })));

    private async Task<HttpClient> CreateClientAsync(WebApplicationFactory<Program> factory)
    {
        await using var db = fixture.CreateDbContext();
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var email = $"{userId:N}@example.com";
        db.Users.Add(new ApplicationUser
        {
            Id = userId, Email = email, NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true, IsActive = true, DisplayName = "Discovery rate limit test"
        });
        var now = DateTime.UtcNow;
        db.AuthSessions.Add(new AuthSession { Id = sessionId, UserId = userId, CreatedAtUtc = now, ExpiresAtUtc = now.AddHours(1) });
        await db.SaveChangesAsync();
        var client = factory.CreateClient();
        var token = factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(userId, sessionId, ["Customer"]).Value;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
