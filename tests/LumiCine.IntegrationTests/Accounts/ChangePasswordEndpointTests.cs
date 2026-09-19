using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LumiCine.Infrastructure.Identity;
using LumiCine.Infrastructure.Persistence;
using LumiCine.IntegrationTests.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LumiCine.IntegrationTests.Accounts;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class ChangePasswordEndpointTests(PostgresDatabaseFixture fixture)
{
    [Fact]
    public async Task Successful_change_revokes_all_sessions_and_invalidates_reset_tokens()
    {
        using var factory = CreateFactory();
        var user = await CreateUserAsync(factory, $"change-password-{Guid.NewGuid():N}@example.test");
        using var firstClient = CreateHttpsClient(factory);
        using var secondClient = CreateHttpsClient(factory);
        var firstLogin = await LoginAsync(firstClient, user.Email!, "SecurePass123");
        var secondLogin = await LoginAsync(secondClient, user.Email!, "SecurePass123");
        string resetToken;
        using (var scope = factory.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var current = await manager.FindByIdAsync(user.Id.ToString("D"));
            Assert.NotNull(current);
            resetToken = await manager.GeneratePasswordResetTokenAsync(current);
        }

        using var changed = await ChangePasswordAsync(firstClient, firstLogin.AccessToken, "SecurePass123", "NewSecure456");
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        Assert.Contains("no-store", changed.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(changed.Headers.Contains("Set-Cookie"));
        await AssertAccessRejectedAsync(firstClient, firstLogin.AccessToken);
        await AssertAccessRejectedAsync(secondClient, secondLogin.AccessToken);
        using var firstRefresh = await RefreshAsync(firstClient, firstLogin.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, firstRefresh.StatusCode);
        using var secondRefresh = await RefreshAsync(secondClient, secondLogin.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, secondRefresh.StatusCode);

        var oldPassword = await LoginAsyncExpectStatusAsync(firstClient, user.Email!, "SecurePass123", HttpStatusCode.Unauthorized);
        using var oldPasswordResponse = oldPassword.Response;

        var newPassword = await LoginAsyncExpectStatusAsync(firstClient, user.Email!, "NewSecure456", HttpStatusCode.OK);
        using var newPasswordResponse = newPassword.Response;

        using (var scope = factory.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var current = await manager.FindByIdAsync(user.Id.ToString("D"));
            Assert.NotNull(current);
            var reset = await manager.ResetPasswordAsync(current, resetToken, "AnotherSecure789");
            Assert.False(reset.Succeeded);
            Assert.Contains(reset.Errors, error => error.Code == "InvalidToken");
        }
    }

    [Fact]
    public async Task Invalid_current_or_new_password_returns_same_400_without_changing_account_or_sessions()
    {
        using var factory = CreateFactory();
        var user = await CreateUserAsync(factory, $"change-password-invalid-{Guid.NewGuid():N}@example.test");
        using var client = CreateHttpsClient(factory);
        var login = await LoginAsync(client, user.Email!, "SecurePass123");
        string passwordHash;
        await using (var db = fixture.CreateDbContext())
        {
            passwordHash = (await db.Users.SingleAsync(item => item.Id == user.Id)).PasswordHash!;
        }

        using var wrongCurrent = await ChangePasswordAsync(client, login.AccessToken, "wrong-password", "NewSecure456");
        using var weakNew = await ChangePasswordAsync(client, login.AccessToken, "SecurePass123", "weak-password");
        Assert.Equal(HttpStatusCode.BadRequest, wrongCurrent.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, weakNew.StatusCode);
        var wrongProblem = await ReadProblemAsync(wrongCurrent);
        var weakProblem = await ReadProblemAsync(weakNew);
        Assert.Equal(wrongProblem, weakProblem);

        await AssertAccessAcceptedAsync(client, login.AccessToken);
        using var refresh = await RefreshAsync(client, login.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        await using var after = fixture.CreateDbContext();
        var saved = await after.Users.SingleAsync(item => item.Id == user.Id);
        Assert.Equal(passwordHash, saved.PasswordHash);
        Assert.Null(await after.AuthSessions.Where(session => session.UserId == user.Id).Select(session => session.RevokedAtUtc).SingleAsync());
    }

    [Fact]
    public async Task Change_password_requires_bearer_authentication()
    {
        using var factory = CreateFactory();
        using var client = CreateHttpsClient(factory);

        using var response = await client.PostAsJsonAsync("/api/users/me/change-password", new
        {
            currentPassword = "SecurePass123",
            newPassword = "NewSecure456"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
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
            IsActive = true, DisplayName = "Change Password Test User", LockoutEnabled = true
        };
        var created = await manager.CreateAsync(user, "SecurePass123");
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(error => error.Description)));
        await manager.AddToRoleAsync(user, "Customer");
        return user;
    }

    private static async Task<LoginData> LoginAsync(HttpClient client, string email, string password)
    {
        var response = await LoginAsyncExpectStatusAsync(client, email, password, HttpStatusCode.OK);
        using (response.Response)
        {
            return response.LoginData!;
        }
    }

    private static async Task<(HttpResponseMessage Response, LoginData? LoginData)> LoginAsyncExpectStatusAsync(
        HttpClient client,
        string email,
        string password,
        HttpStatusCode expectedStatus)
    {
        using var csrfResponse = await client.GetAsync("/api/auth/csrf");
        csrfResponse.EnsureSuccessStatusCode();
        using var csrfBody = await JsonDocument.ParseAsync(await csrfResponse.Content.ReadAsStreamAsync());
        var csrfCookie = Assert.Single(csrfResponse.Headers.GetValues("Set-Cookie")).Split(';', 2)[0];
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email, password })
        };
        request.Headers.Add("X-CSRF-TOKEN", csrfBody.RootElement.GetProperty("requestToken").GetString());
        request.Headers.Add("Cookie", csrfCookie);
        var response = await client.SendAsync(request);
        Assert.Equal(expectedStatus, response.StatusCode);
        if (expectedStatus != HttpStatusCode.OK)
        {
            return (response, null);
        }

        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var tokenCookie = Assert.Single(response.Headers.GetValues("Set-Cookie")).Split(';', 2)[0];
        return (response, new LoginData(
            body.RootElement.GetProperty("accessToken").GetString()!,
            tokenCookie[("__Secure-LumiCine.Refresh=".Length)..]));
    }

    private static async Task<HttpResponseMessage> ChangePasswordAsync(HttpClient client, string accessToken, string currentPassword, string newPassword)
    {
        using var request = AuthorizedRequest(HttpMethod.Post, "/api/users/me/change-password", accessToken);
        request.Content = JsonContent.Create(new { currentPassword, newPassword });
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> RefreshAsync(HttpClient client, string refreshToken)
    {
        using var csrfResponse = await client.GetAsync("/api/auth/csrf");
        csrfResponse.EnsureSuccessStatusCode();
        using var csrfBody = await JsonDocument.ParseAsync(await csrfResponse.Content.ReadAsStreamAsync());
        var csrfCookie = Assert.Single(csrfResponse.Headers.GetValues("Set-Cookie")).Split(';', 2)[0];
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("X-CSRF-TOKEN", csrfBody.RootElement.GetProperty("requestToken").GetString());
        request.Headers.Add("Cookie", $"{csrfCookie}; __Secure-LumiCine.Refresh={refreshToken}");
        return await client.SendAsync(request);
    }

    private static async Task AssertAccessAcceptedAsync(HttpClient client, string accessToken)
    {
        using var response = await client.SendAsync(AuthorizedRequest(HttpMethod.Get, "/_test/auth/authenticated", accessToken));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task AssertAccessRejectedAsync(HttpClient client, string accessToken)
    {
        using var response = await client.SendAsync(AuthorizedRequest(HttpMethod.Get, "/_test/auth/authenticated", accessToken));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static HttpRequestMessage AuthorizedRequest(HttpMethod method, string path, string accessToken)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private static async Task<string> ReadProblemAsync(HttpResponseMessage response)
    {
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return body.RootElement.GetProperty("title").GetString()!;
    }

    private sealed record LoginData(string AccessToken, string RefreshToken);
}
