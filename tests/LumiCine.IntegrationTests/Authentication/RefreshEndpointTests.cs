using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LumiCine.Infrastructure.Authentication;
using LumiCine.Infrastructure.Identity;
using LumiCine.Infrastructure.Persistence;
using LumiCine.IntegrationTests.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace LumiCine.IntegrationTests.Authentication;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class RefreshEndpointTests(PostgresDatabaseFixture fixture)
{
    private const string CookieName = "__Secure-LumiCine.Refresh";

    [Fact]
    public async Task Refresh_rotates_hashed_token_and_keeps_absolute_session_expiry()
    {
        using var factory = CreateFactory();
        var user = await CreateUserAsync(factory, $"refresh-{Guid.NewGuid():N}@example.test");
        using var client = CreateHttpsClient(factory);
        var (login, originalToken) = await LoginAsync(client, user.Email!);
        var oldAccessToken = await ReadAccessTokenAsync(login);
        var oldSessionId = ReadSessionId(oldAccessToken);
        DateTime originalExpiry;
        await using (var db = fixture.CreateDbContext())
        {
            originalExpiry = await db.AuthSessions.Where(session => session.Id == oldSessionId).Select(session => session.ExpiresAtUtc).SingleAsync();
        }

        using var response = await RefreshAsync(client, originalToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
        var rotatedToken = ReadCookie(response);
        Assert.NotEqual(originalToken, rotatedToken);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var accessToken = body.RootElement.GetProperty("accessToken").GetString()!;
        Assert.DoesNotContain(rotatedToken, body.RootElement.ToString(), StringComparison.Ordinal);
        Assert.Equal(oldSessionId, ReadSessionId(accessToken));
        await using (var db = fixture.CreateDbContext())
        {
            var session = await db.AuthSessions.SingleAsync(item => item.Id == oldSessionId);
            Assert.Equal(originalExpiry, session.ExpiresAtUtc);
            var tokens = await db.RefreshTokens.Where(item => item.SessionId == oldSessionId).OrderBy(item => item.CreatedAtUtc).ToListAsync();
            Assert.Equal(2, tokens.Count);
            Assert.NotNull(tokens[0].ConsumedAtUtc);
            Assert.Null(tokens[1].ConsumedAtUtc);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rotatedToken))), tokens[1].Hash);
            Assert.DoesNotContain(rotatedToken, tokens.Select(token => token.Hash));
        }
    }

    [Fact]
    public async Task Replaying_consumed_token_revokes_session_and_both_access_tokens()
    {
        using var factory = CreateFactory();
        var user = await CreateUserAsync(factory, $"replay-{Guid.NewGuid():N}@example.test");
        using var client = CreateHttpsClient(factory);
        var (login, originalToken) = await LoginAsync(client, user.Email!);
        var originalAccessToken = await ReadAccessTokenAsync(login);
        using var firstRefresh = await RefreshAsync(client, originalToken);
        var currentToken = ReadCookie(firstRefresh);
        using var body = await JsonDocument.ParseAsync(await firstRefresh.Content.ReadAsStreamAsync());
        var rotatedAccessToken = body.RootElement.GetProperty("accessToken").GetString()!;
        using var replay = await RefreshAsync(client, originalToken);

        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Contains("no-store", replay.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
        await AssertSessionAccessRejectedAsync(client, originalAccessToken);
        await AssertSessionAccessRejectedAsync(client, rotatedAccessToken);
        await using var db = fixture.CreateDbContext();
        var session = await db.AuthSessions.SingleAsync(item => item.Id == ReadSessionId(originalAccessToken));
        Assert.NotNull(session.RevokedAtUtc);
        Assert.True(await db.RefreshTokens.Where(token => token.Hash == Hash(currentToken)).AnyAsync());
    }

    [Fact]
    public async Task Concurrent_refreshes_allow_one_rotation_and_revoke_family_for_loser()
    {
        using var factory = CreateFactory();
        var user = await CreateUserAsync(factory, $"parallel-refresh-{Guid.NewGuid():N}@example.test");
        using var loginClient = CreateHttpsClient(factory);
        var (login, originalToken) = await LoginAsync(loginClient, user.Email!);
        var accessToken = await ReadAccessTokenAsync(login);
        using var firstClient = CreateHttpsClient(factory);
        using var secondClient = CreateHttpsClient(factory);
        var requests = await Task.WhenAll(
            RefreshAsync(firstClient, originalToken),
            RefreshAsync(secondClient, originalToken));

        Assert.Single(requests, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(requests, response => response.StatusCode == HttpStatusCode.Unauthorized);
        foreach (var response in requests)
        {
            response.Dispose();
        }

        await AssertSessionAccessRejectedAsync(loginClient, accessToken);
        await using var db = fixture.CreateDbContext();
        var session = await db.AuthSessions.SingleAsync(item => item.Id == ReadSessionId(accessToken));
        Assert.NotNull(session.RevokedAtUtc);
        Assert.Equal(2, await db.RefreshTokens.CountAsync(token => token.SessionId == session.Id));
    }

    [Fact]
    public async Task Unknown_expired_and_inactive_tokens_are_rejected_without_affecting_other_sessions()
    {
        using var factory = CreateFactory();
        var user = await CreateUserAsync(factory, $"refresh-invalid-{Guid.NewGuid():N}@example.test");
        using var client = CreateHttpsClient(factory);
        var (firstLogin, firstToken) = await LoginAsync(client, user.Email!);
        var firstAccess = await ReadAccessTokenAsync(firstLogin);
        var (secondLogin, secondToken) = await LoginAsync(client, user.Email!);
        var secondAccess = await ReadAccessTokenAsync(secondLogin);

        using var unknown = await RefreshAsync(client, "unknown-refresh-value");
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        await AssertSessionAccessAcceptedAsync(client, firstAccess);

        await using (var db = fixture.CreateDbContext())
        {
            var sessionId = ReadSessionId(firstAccess);
            var token = await db.RefreshTokens.SingleAsync(item => item.Hash == Hash(secondToken));
            token.CreatedAtUtc = DateTime.UtcNow.AddDays(-2);
            token.ExpiresAtUtc = DateTime.UtcNow.AddDays(-1);
            await db.SaveChangesAsync();
            var otherSession = await db.AuthSessions.SingleAsync(item => item.Id == sessionId);
            Assert.Null(otherSession.RevokedAtUtc);
        }

        using var expired = await RefreshAsync(client, secondToken);
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        await AssertSessionAccessAcceptedAsync(client, firstAccess);
        await using (var db = fixture.CreateDbContext())
        {
            var inactive = await db.Users.SingleAsync(item => item.Id == user.Id);
            inactive.IsActive = false;
            await db.SaveChangesAsync();
        }

        using var inactiveResponse = await RefreshAsync(client, firstToken);
        Assert.Equal(HttpStatusCode.Unauthorized, inactiveResponse.StatusCode);
        await AssertSessionAccessRejectedAsync(client, secondAccess);
        await using (var db = fixture.CreateDbContext())
        {
            var unconfirmed = await db.Users.SingleAsync(item => item.Id == user.Id);
            unconfirmed.IsActive = true;
            unconfirmed.EmailConfirmed = false;
            await db.SaveChangesAsync();
        }

        using var unconfirmedResponse = await RefreshAsync(client, firstToken);
        Assert.Equal(HttpStatusCode.Unauthorized, unconfirmedResponse.StatusCode);
    }

    [Fact]
    public async Task Refresh_requires_https_and_csrf()
    {
        using var factory = CreateFactory();
        using var httpsClient = CreateHttpsClient(factory);
        using var missingCsrf = await httpsClient.PostAsync("/api/auth/refresh", null);
        using var httpClient = factory.CreateClient();
        using var httpResponse = await httpClient.PostAsync("/api/auth/refresh", null);

        AssertProblem(missingCsrf, HttpStatusCode.BadRequest, "Invalid CSRF token");
        AssertProblem(httpResponse, HttpStatusCode.BadRequest, "HTTPS required");
    }

    private ApiWebApplicationFactory CreateFactory() => new(connectionString: fixture.ConnectionString, includeTestControllers: true);

    private static HttpClient CreateHttpsClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false });

    private async Task<ApplicationUser> CreateUserAsync(ApiWebApplicationFactory factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(), UserName = email, Email = email, EmailConfirmed = true,
            IsActive = true, DisplayName = "Refresh Test User", LockoutEnabled = true
        };
        var created = await manager.CreateAsync(user, "SecurePass123");
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(error => error.Description)));
        await manager.AddToRoleAsync(user, "Customer");
        return user;
    }

    private static async Task<(HttpResponseMessage Response, string Token)> LoginAsync(HttpClient client, string email)
    {
        var (csrf, csrfCookie) = await GetCsrfTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = JsonContent.Create(new { email, password = "SecurePass123" }) };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        request.Headers.Add("Cookie", csrfCookie);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (response, ReadCookie(response));
    }

    private static async Task<HttpResponseMessage> RefreshAsync(HttpClient client, string token)
    {
        var (csrf, csrfCookie) = await GetCsrfTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        request.Headers.Add("Cookie", $"{csrfCookie}; {CookieName}={token}");
        return await client.SendAsync(request);
    }

    private static async Task<(string Token, string Cookie)> GetCsrfTokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/auth/csrf");
        response.EnsureSuccessStatusCode();
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie")).Split(';', 2)[0];
        return (body.RootElement.GetProperty("requestToken").GetString()!, cookie);
    }

    private static async Task<string> ReadAccessTokenAsync(HttpResponseMessage response)
    {
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return body.RootElement.GetProperty("accessToken").GetString()!;
    }

    private static Guid ReadSessionId(string accessToken) =>
        Guid.Parse(new JsonWebTokenHandler().ReadJsonWebToken(accessToken).GetClaim(JwtRegisteredClaimNames.Sid).Value);

    private static string ReadCookie(HttpResponseMessage response)
    {
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains(CookieName + "=", cookie, StringComparison.Ordinal);
        Assert.Contains("path=/api/auth", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        return cookie.Split(';', 2)[0][(CookieName.Length + 1)..];
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static async Task AssertSessionAccessAcceptedAsync(HttpClient client, string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/_test/auth/authenticated");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task AssertSessionAccessRejectedAsync(HttpClient client, string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/_test/auth/authenticated");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static void AssertProblem(HttpResponseMessage response, HttpStatusCode statusCode, string title)
    {
        Assert.Equal(statusCode, response.StatusCode);
        using var body = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
        Assert.Equal(title, body.RootElement.GetProperty("title").GetString());
    }
}
