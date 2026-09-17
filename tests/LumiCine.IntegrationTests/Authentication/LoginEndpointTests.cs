using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LumiCine.Application.Accounts;
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
public sealed class LoginEndpointTests(PostgresDatabaseFixture fixture)
{
    [Fact]
    public async Task Login_issues_access_token_and_stores_only_refresh_hash_in_secure_cookie()
    {
        using var factory = CreateFactory();
        var user = await CreateUserAsync(factory, $"login-{Guid.NewGuid():N}@example.test", roles: [RoleNames.Customer]);
        using var client = CreateHttpsClient(factory);

        using var response = await LoginAsync(client, new
        {
            email = $" {user.Email!.ToUpperInvariant()} ",
            password = "SecurePass123",
            role = "Admin"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("__Secure-LumiCine.Refresh=", cookie, StringComparison.Ordinal);
        Assert.Contains("path=/api/auth", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);
        var rawRefreshToken = cookie.Split(';', 2)[0]["__Secure-LumiCine.Refresh=".Length..];
        using var responseBody = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var json = responseBody.RootElement;
        var accessToken = json.GetProperty("accessToken").GetString()!;
        Assert.Equal("Bearer", json.GetProperty("tokenType").GetString());
        Assert.Equal(900, json.GetProperty("expiresIn").GetInt32());
        Assert.Equal(user.Id, json.GetProperty("user").GetProperty("id").GetGuid());
        Assert.Equal(user.Email, json.GetProperty("user").GetProperty("email").GetString());
        Assert.False(json.GetProperty("user").TryGetProperty("passwordHash", out _));
        Assert.DoesNotContain(rawRefreshToken, json.ToString(), StringComparison.Ordinal);

        var token = new JsonWebTokenHandler().ReadJsonWebToken(accessToken);
        Assert.Equal(user.Id.ToString("D"), token.Subject);
        var sessionId = Guid.Parse(token.GetClaim(JwtRegisteredClaimNames.Sid).Value);
        Assert.Equal([RoleNames.Customer], token.Claims.Where(claim => claim.Type == "role").Select(claim => claim.Value).ToArray());
        await using var db = fixture.CreateDbContext();
        var session = await db.AuthSessions.SingleAsync(item => item.Id == sessionId);
        Assert.Equal(user.Id, session.UserId);
        Assert.InRange(session.ExpiresAtUtc - session.CreatedAtUtc, TimeSpan.FromDays(7), TimeSpan.FromDays(7));
        var refresh = await db.RefreshTokens.SingleAsync(item => item.SessionId == sessionId);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawRefreshToken))), refresh.Hash);
        Assert.NotEqual(rawRefreshToken, refresh.Hash);
    }

    [Fact]
    public async Task Unknown_wrong_unconfirmed_inactive_and_locked_accounts_share_the_same_401()
    {
        using var factory = CreateFactory();
        var wrongPassword = await CreateUserAsync(factory, $"wrong-{Guid.NewGuid():N}@example.test");
        var unconfirmed = await CreateUserAsync(factory, $"unconfirmed-{Guid.NewGuid():N}@example.test", confirmed: false);
        var inactive = await CreateUserAsync(factory, $"inactive-{Guid.NewGuid():N}@example.test", active: false);
        var locked = await CreateUserAsync(factory, $"locked-{Guid.NewGuid():N}@example.test", locked: true);
        using var client = CreateHttpsClient(factory);
        var attempts = new[]
        {
            ("unknown@example.test", "SecurePass123"),
            (wrongPassword.Email!, "x"),
            (unconfirmed.Email!, "SecurePass123"),
            (inactive.Email!, "SecurePass123"),
            (locked.Email!, "SecurePass123")
        };
        var responseTexts = new List<string>();

        foreach (var (email, password) in attempts)
        {
            using var response = await LoginAsync(client, new { email, password });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
            responseTexts.Add(await response.Content.ReadAsStringAsync());
        }

        var normalizedResponses = responseTexts.Select(RemoveTraceId).ToArray();
        Assert.All(normalizedResponses, text => Assert.Equal(normalizedResponses[0], text));
        await using var db = fixture.CreateDbContext();
        Assert.Empty(await db.AuthSessions.Where(session =>
            session.UserId == wrongPassword.Id || session.UserId == unconfirmed.Id || session.UserId == inactive.Id || session.UserId == locked.Id).ToListAsync());
    }

    [Fact]
    public async Task Five_failed_passwords_persist_lockout_and_correct_password_stays_rejected()
    {
        using var factory = CreateFactory();
        var user = await CreateUserAsync(factory, $"lockout-{Guid.NewGuid():N}@example.test");
        using var client = CreateHttpsClient(factory);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var response = await LoginAsync(client, new { email = user.Email, password = "wrong-password" });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        await using (var db = fixture.CreateDbContext())
        {
            var storedUser = await db.Users.SingleAsync(item => item.Id == user.Id);
            Assert.True(storedUser.LockoutEnd > DateTimeOffset.UtcNow);
        }

        using var correctPassword = await LoginAsync(client, new { email = user.Email, password = "SecurePass123" });
        Assert.Equal(HttpStatusCode.Unauthorized, correctPassword.StatusCode);
    }

    [Fact]
    public async Task Successful_login_resets_failed_password_count()
    {
        using var factory = CreateFactory();
        var user = await CreateUserAsync(factory, $"reset-count-{Guid.NewGuid():N}@example.test");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var scope = factory.Services.CreateScope();
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var currentUser = await manager.FindByIdAsync(user.Id.ToString("D"));
            Assert.NotNull(currentUser);
            await manager.AccessFailedAsync(currentUser);
        }

        using var client = CreateHttpsClient(factory);
        using var response = await LoginAsync(client, new { email = user.Email, password = "SecurePass123" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(0, (await db.Users.SingleAsync(item => item.Id == user.Id)).AccessFailedCount);
    }

    [Fact]
    public async Task Login_requires_https_and_csrf()
    {
        using var factory = CreateFactory();
        using var httpsClient = CreateHttpsClient(factory);
        using var missingCsrf = await httpsClient.PostAsJsonAsync("/api/auth/login", new { email = "user@example.test", password = "SecurePass123" });
        using var httpClient = factory.CreateClient();
        using var httpResponse = await httpClient.PostAsJsonAsync("/api/auth/login", new { email = "user@example.test", password = "SecurePass123" });

        await AssertProblemAsync(missingCsrf, HttpStatusCode.BadRequest, "Invalid CSRF token");
        await AssertProblemAsync(httpResponse, HttpStatusCode.BadRequest, "HTTPS required");
    }

    private ApiWebApplicationFactory CreateFactory() => new(connectionString: fixture.ConnectionString);

    private static HttpClient CreateHttpsClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, object payload)
    {
        using var tokenResponse = await client.GetAsync("/api/auth/csrf");
        tokenResponse.EnsureSuccessStatusCode();
        using var tokenBody = await JsonDocument.ParseAsync(await tokenResponse.Content.ReadAsStreamAsync());
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add("X-CSRF-TOKEN", tokenBody.RootElement.GetProperty("requestToken").GetString());
        using (request)
        {
            return await client.SendAsync(request);
        }
    }

    private async Task<ApplicationUser> CreateUserAsync(
        ApiWebApplicationFactory factory,
        string email,
        bool confirmed = true,
        bool active = true,
        bool locked = false,
        IReadOnlyCollection<string>? roles = null)
    {
        using var scope = factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            EmailConfirmed = confirmed,
            IsActive = active,
            DisplayName = "Login Test User",
            LockoutEnabled = true
        };
        var created = await manager.CreateAsync(user, "SecurePass123");
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(error => error.Description)));
        if (roles is not null)
        {
            var assigned = await manager.AddToRolesAsync(user, roles);
            Assert.True(assigned.Succeeded, string.Join("; ", assigned.Errors.Select(error => error.Description)));
        }

        if (locked)
        {
            var lockout = await manager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddMinutes(10));
            Assert.True(lockout.Succeeded);
        }

        return user;
    }

    private static string RemoveTraceId(string json)
    {
        using var document = JsonDocument.Parse(json);
        var fields = document.RootElement.EnumerateObject()
            .Where(property => property.Name != "traceId")
            .Select(property => new KeyValuePair<string, JsonElement>(property.Name, property.Value.Clone()));
        return JsonSerializer.Serialize(fields.ToDictionary(pair => pair.Key, pair => pair.Value));
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode statusCode, string title)
    {
        Assert.Equal(statusCode, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal((int)statusCode, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(title, body.RootElement.GetProperty("title").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("traceId").GetString()));
    }
}
