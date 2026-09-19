using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LumiCine.Infrastructure.Identity;
using LumiCine.IntegrationTests.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace LumiCine.IntegrationTests.Authentication;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class LogoutEndpointTests(PostgresDatabaseFixture fixture)
{
    private const string CookieName = "__Secure-LumiCine.Refresh";

    [Fact]
    public async Task Logout_revokes_only_cookie_session_and_clears_cookie_idempotently()
    {
        using var factory = CreateFactory();
        var user = await CreateUserAsync(factory, $"logout-{Guid.NewGuid():N}@example.test");
        using var client = CreateHttpsClient(factory);
        var (firstLogin, firstToken) = await LoginAsync(client, user.Email!);
        var firstAccess = await ReadAccessTokenAsync(firstLogin);
        var (secondLogin, secondToken) = await LoginAsync(client, user.Email!);
        var secondAccess = await ReadAccessTokenAsync(secondLogin);

        using var logout = await LogoutAsync(client, firstToken);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Contains("no-store", logout.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
        var clearingCookie = Assert.Single(logout.Headers.GetValues("Set-Cookie"));
        Assert.Contains(CookieName + "=", clearingCookie, StringComparison.Ordinal);
        Assert.Contains("path=/api/auth", clearingCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", clearingCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", clearingCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", clearingCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expires=", clearingCookie, StringComparison.OrdinalIgnoreCase);
        await AssertAccessRejectedAsync(client, firstAccess);
        await AssertAccessAcceptedAsync(client, secondAccess);

        using var repeated = await LogoutAsync(client, firstToken);
        Assert.Equal(HttpStatusCode.NoContent, repeated.StatusCode);
        using var missing = await LogoutAsync(client, null);
        Assert.Equal(HttpStatusCode.NoContent, missing.StatusCode);
        using var unknown = await LogoutAsync(client, "unknown-refresh-value");
        Assert.Equal(HttpStatusCode.NoContent, unknown.StatusCode);
        await AssertAccessAcceptedAsync(client, secondAccess);
    }

    [Fact]
    public async Task Logout_requires_https_and_csrf()
    {
        using var factory = CreateFactory();
        using var httpsClient = CreateHttpsClient(factory);
        using var missingCsrf = await httpsClient.PostAsync("/api/auth/logout", null);
        using var httpClient = factory.CreateClient();
        using var httpResponse = await httpClient.PostAsync("/api/auth/logout", null);

        AssertProblem(missingCsrf, "Invalid CSRF token");
        AssertProblem(httpResponse, "HTTPS required");
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
            IsActive = true, DisplayName = "Logout Test User", LockoutEnabled = true
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

    private static async Task<HttpResponseMessage> LogoutAsync(HttpClient client, string? token)
    {
        var (csrf, csrfCookie) = await GetCsrfTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        request.Headers.Add("Cookie", token is null ? csrfCookie : $"{csrfCookie}; {CookieName}={token}");
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

    private static string ReadCookie(HttpResponseMessage response) =>
        Assert.Single(response.Headers.GetValues("Set-Cookie")).Split(';', 2)[0][(CookieName.Length + 1)..];

    private static async Task<string> ReadAccessTokenAsync(HttpResponseMessage response)
    {
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return body.RootElement.GetProperty("accessToken").GetString()!;
    }

    private static async Task AssertAccessAcceptedAsync(HttpClient client, string accessToken)
    {
        using var request = AuthenticatedRequest(accessToken);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task AssertAccessRejectedAsync(HttpClient client, string accessToken)
    {
        using var request = AuthenticatedRequest(accessToken);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static HttpRequestMessage AuthenticatedRequest(string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/_test/auth/authenticated");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private static void AssertProblem(HttpResponseMessage response, string title)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
        Assert.Equal(title, body.RootElement.GetProperty("title").GetString());
    }
}
