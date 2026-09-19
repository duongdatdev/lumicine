using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LumiCine.Infrastructure.Identity;
using LumiCine.IntegrationTests.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LumiCine.IntegrationTests.Authentication;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class ResetPasswordEndpointTests(PostgresDatabaseFixture fixture)
{
    [Fact]
    public async Task Reset_changes_password_once_and_revokes_every_session_atomically()
    {
        using var factory = CreateFactory();
        var user = await CreateUserAsync(factory, $"reset-password-{Guid.NewGuid():N}@example.test");
        using var firstClient = CreateHttpsClient(factory);
        using var secondClient = CreateHttpsClient(factory);
        var first = await LoginAsync(firstClient, user.Email!, "SecurePass123");
        var second = await LoginAsync(secondClient, user.Email!, "SecurePass123");
        var code = await GenerateCodeAsync(factory, user, passwordReset: true);

        var attempts = await Task.WhenAll(
            ResetAsync(firstClient, user.Email!, code, "NewSecure456"),
            ResetAsync(secondClient, user.Email!, code, "NewSecure456"));

        Assert.Single(attempts, response => response.StatusCode == HttpStatusCode.NoContent);
        Assert.Single(attempts, response => response.StatusCode == HttpStatusCode.BadRequest);
        Assert.All(attempts, response => Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase));
        foreach (var response in attempts)
        {
            response.Dispose();
        }

        await AssertAccessRejectedAsync(firstClient, first.AccessToken);
        await AssertAccessRejectedAsync(secondClient, second.AccessToken);
        using var firstRefresh = await RefreshAsync(firstClient, first.RefreshToken);
        using var secondRefresh = await RefreshAsync(secondClient, second.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, firstRefresh.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, secondRefresh.StatusCode);
        var oldPassword = await LoginAsyncExpectStatusAsync(firstClient, user.Email!, "SecurePass123", HttpStatusCode.Unauthorized);
        using var oldPasswordResponse = oldPassword.Response;
        var newPassword = await LoginAsyncExpectStatusAsync(firstClient, user.Email!, "NewSecure456", HttpStatusCode.OK);
        using var newPasswordResponse = newPassword.Response;
    }

    [Fact]
    public async Task Malformed_wrong_purpose_unknown_ineligible_and_policy_failures_share_generic_bad_request()
    {
        using var factory = CreateFactory();
        var eligible = await CreateUserAsync(factory, $"reset-eligible-{Guid.NewGuid():N}@example.test");
        var inactive = await CreateUserAsync(factory, $"reset-inactive-{Guid.NewGuid():N}@example.test", active: false);
        var unconfirmed = await CreateUserAsync(factory, $"reset-unconfirmed-{Guid.NewGuid():N}@example.test", confirmed: false);
        var validResetCode = await GenerateCodeAsync(factory, eligible, passwordReset: true);
        var inactiveResetCode = await GenerateCodeAsync(factory, inactive, passwordReset: true);
        var unconfirmedResetCode = await GenerateCodeAsync(factory, unconfirmed, passwordReset: true);
        var wrongPurposeCode = await GenerateCodeAsync(factory, eligible, passwordReset: false);
        using var client = CreateHttpsClient(factory);
        var badResponses = new[]
        {
            await ResetAsync(client, eligible.Email!, "not***base64", "NewSecure456"),
            await ResetAsync(client, eligible.Email!, wrongPurposeCode, "NewSecure456"),
            await ResetAsync(client, "unknown@example.test", validResetCode, "NewSecure456"),
            await ResetAsync(client, inactive.Email!, inactiveResetCode, "NewSecure456"),
            await ResetAsync(client, unconfirmed.Email!, unconfirmedResetCode, "NewSecure456"),
            await ResetAsync(client, eligible.Email!, validResetCode, "weak-password")
        };

        Assert.All(badResponses, response => Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode));
        var problems = await Task.WhenAll(badResponses.Select(ReadGenericProblemAsync));
        Assert.All(problems, problem => Assert.Equal(problems[0], problem));
        foreach (var response in badResponses)
        {
            response.Dispose();
        }

        await using var db = fixture.CreateDbContext();
        Assert.Equal(eligible.PasswordHash, (await db.Users.SingleAsync(candidate => candidate.Id == eligible.Id)).PasswordHash);
        Assert.Equal(inactive.PasswordHash, (await db.Users.SingleAsync(candidate => candidate.Id == inactive.Id)).PasswordHash);
        Assert.Equal(unconfirmed.PasswordHash, (await db.Users.SingleAsync(candidate => candidate.Id == unconfirmed.Id)).PasswordHash);
        Assert.True(await db.Users.AnyAsync(candidate => candidate.Id == inactive.Id));
        Assert.True(await db.Users.AnyAsync(candidate => candidate.Id == unconfirmed.Id));
        Assert.Equal(eligible.PasswordHash, (await db.Users.SingleAsync(candidate => candidate.Id == eligible.Id)).PasswordHash);
    }

    [Fact]
    public async Task Expired_reset_token_and_missing_https_or_csrf_are_rejected()
    {
        using var expiredFactory = CreateFactory(expirePasswordResetTokens: true);
        var user = await CreateUserAsync(expiredFactory, $"reset-expired-{Guid.NewGuid():N}@example.test");
        var code = await GenerateCodeAsync(expiredFactory, user, passwordReset: true);
        using var client = CreateHttpsClient(expiredFactory);
        using var expired = await ResetAsync(client, user.Email!, code, "NewSecure456");
        Assert.Equal(HttpStatusCode.BadRequest, expired.StatusCode);
        Assert.Contains("no-store", expired.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);

        using var missingCsrf = await client.PostAsJsonAsync("/api/auth/reset-password", new
        {
            email = user.Email, code, newPassword = "NewSecure456"
        });
        using var httpClient = expiredFactory.CreateClient();
        using var httpResponse = await httpClient.PostAsJsonAsync("/api/auth/reset-password", new
        {
            email = user.Email, code, newPassword = "NewSecure456"
        });
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, httpResponse.StatusCode);
        Assert.Equal("Invalid CSRF token", await ReadProblemTitleAsync(missingCsrf));
        Assert.Equal("HTTPS required", await ReadProblemTitleAsync(httpResponse));
    }

    private WebApplicationFactory<Program> CreateFactory(bool expirePasswordResetTokens = false) =>
        new ApiWebApplicationFactory(connectionString: fixture.ConnectionString, includeTestControllers: true).WithWebHostBuilder(builder =>
        {
            if (expirePasswordResetTokens)
            {
                builder.ConfigureTestServices(services =>
                    services.Configure<PasswordResetTokenOptions>(options => options.TokenLifespan = TimeSpan.FromSeconds(-1)));
            }
        });

    private static HttpClient CreateHttpsClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false });

    private async Task<ApplicationUser> CreateUserAsync(
        WebApplicationFactory<Program> factory,
        string email,
        bool confirmed = true,
        bool active = true)
    {
        using var scope = factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(), UserName = email, Email = email,
            EmailConfirmed = confirmed, IsActive = active, DisplayName = "Reset Password Test"
        };
        var created = await manager.CreateAsync(user, "SecurePass123");
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(error => error.Description)));
        return user;
    }

    private static async Task<LoginData> LoginAsync(HttpClient client, string email, string password)
    {
        var result = await LoginAsyncExpectStatusAsync(client, email, password, HttpStatusCode.OK);
        using (result.Response)
        {
            return result.Login!;
        }
    }

    private static async Task<(HttpResponseMessage Response, LoginData? Login)> LoginAsyncExpectStatusAsync(
        HttpClient client, string email, string password, HttpStatusCode expected)
    {
        var (csrf, cookie) = await GetCsrfAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email, password })
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        request.Headers.Add("Cookie", cookie);
        var response = await client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
        if (expected != HttpStatusCode.OK)
        {
            return (response, null);
        }

        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var refreshCookie = Assert.Single(response.Headers.GetValues("Set-Cookie")).Split(';', 2)[0];
        return (response, new LoginData(
            body.RootElement.GetProperty("accessToken").GetString()!,
            refreshCookie["__Secure-LumiCine.Refresh=".Length..]));
    }

    private static async Task<HttpResponseMessage> ResetAsync(HttpClient client, string email, string code, string newPassword)
    {
        var (csrf, cookie) = await GetCsrfAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reset-password")
        {
            Content = JsonContent.Create(new { email, code, newPassword })
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        request.Headers.Add("Cookie", cookie);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> RefreshAsync(HttpClient client, string refreshToken)
    {
        var (csrf, cookie) = await GetCsrfAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        request.Headers.Add("Cookie", $"{cookie}; __Secure-LumiCine.Refresh={refreshToken}");
        return await client.SendAsync(request);
    }

    private static async Task<(string Token, string Cookie)> GetCsrfAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/auth/csrf");
        response.EnsureSuccessStatusCode();
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return (body.RootElement.GetProperty("requestToken").GetString()!,
            Assert.Single(response.Headers.GetValues("Set-Cookie")).Split(';', 2)[0]);
    }

    private static async Task AssertAccessRejectedAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/_test/auth/authenticated");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<string> GenerateCodeAsync(WebApplicationFactory<Program> factory, ApplicationUser user, bool passwordReset)
    {
        using var scope = factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var token = passwordReset
            ? await manager.GeneratePasswordResetTokenAsync(user)
            : await manager.GenerateEmailConfirmationTokenAsync(user);
        return WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
    }

    private static async Task<string> ReadGenericProblemAsync(HttpResponseMessage response)
    {
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = body.RootElement;
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal((int)HttpStatusCode.BadRequest, root.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
        return JsonSerializer.Serialize(new { title = root.GetProperty("title").GetString(), detail = root.GetProperty("detail").GetString() });
    }

    private static async Task<string> ReadProblemTitleAsync(HttpResponseMessage response)
    {
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return body.RootElement.GetProperty("title").GetString()!;
    }

    private sealed record LoginData(string AccessToken, string RefreshToken);
}
