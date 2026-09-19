using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LumiCine.Application.Accounts;
using LumiCine.Infrastructure.Authentication;
using LumiCine.Infrastructure.Identity;
using LumiCine.Infrastructure.Persistence;
using LumiCine.IntegrationTests.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LumiCine.IntegrationTests.Admin;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class AdminUserStatusEndpointTests(PostgresDatabaseFixture fixture)
{
    [Fact]
    public async Task Deactivation_revokes_all_sessions_and_blocks_existing_access_token()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var admin = await CreateUserAsync(factory, RoleNames.Admin);
        var customer = await CreateUserAsync(factory, RoleNames.Customer, sessionCount: 2);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", customer.Token);
        using (var forbiddenResponse = await client.PutAsJsonAsync(
                   $"/api/admin/users/{customer.User.Id:D}/status", new { isActive = false }))
        {
            await AssertProblemAsync(forbiddenResponse, HttpStatusCode.Forbidden);
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.Token);

        using var response = await client.PutAsJsonAsync($"/api/admin/users/{customer.User.Id:D}/status", new { isActive = false });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", customer.Token);
        using var blockedResponse = await client.GetAsync("/api/admin/users");
        await AssertProblemAsync(blockedResponse, HttpStatusCode.Unauthorized);
        await using var db = fixture.CreateDbContext();
        var target = await db.Users.AsNoTracking().SingleAsync(user => user.Id == customer.User.Id);
        var sessions = await db.AuthSessions.AsNoTracking().Where(session => session.UserId == customer.User.Id).ToArrayAsync();
        Assert.False(target.IsActive);
        Assert.Equal(2, sessions.Length);
        Assert.All(sessions, session => Assert.NotNull(session.RevokedAtUtc));
    }

    [Fact]
    public async Task Reactivation_clears_lockout_without_resurrecting_old_sessions()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var admin = await CreateUserAsync(factory, RoleNames.Admin);
        var customer = await CreateUserAsync(
            factory,
            RoleNames.Customer,
            lockoutEnd: DateTimeOffset.UtcNow.AddHours(1),
            accessFailedCount: 5);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.Token);

        using (var deactivateResponse = await client.PutAsJsonAsync(
                   $"/api/admin/users/{customer.User.Id:D}/status", new { isActive = false }))
        {
            Assert.Equal(HttpStatusCode.NoContent, deactivateResponse.StatusCode);
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", customer.Token);
        using (var oldTokenResponse = await client.GetAsync("/api/admin/users"))
        {
            await AssertProblemAsync(oldTokenResponse, HttpStatusCode.Unauthorized);
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.Token);
        using (var activateResponse = await client.PutAsJsonAsync(
                   $"/api/admin/users/{customer.User.Id:D}/status", new { isActive = true }))
        {
            Assert.Equal(HttpStatusCode.NoContent, activateResponse.StatusCode);
        }

        await using (var db = fixture.CreateDbContext())
        {
            var target = await db.Users.AsNoTracking().SingleAsync(user => user.Id == customer.User.Id);
            Assert.True(target.IsActive);
            Assert.Null(target.LockoutEnd);
            Assert.Equal(0, target.AccessFailedCount);
            Assert.NotNull(await db.AuthSessions.Where(session => session.Id == customer.SessionId).Select(session => session.RevokedAtUtc).SingleAsync());
        }

        var newSessionId = await CreateSessionAsync(customer.User.Id);
        var freshToken = factory.Services.GetRequiredService<IAccessTokenIssuer>()
            .Issue(customer.User.Id, newSessionId, [RoleNames.Customer]).Value;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", customer.Token);
        using (var oldTokenResponse = await client.GetAsync("/api/admin/users"))
        {
            await AssertProblemAsync(oldTokenResponse, HttpStatusCode.Unauthorized);
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", freshToken);
        using var freshTokenResponse = await client.GetAsync("/api/admin/users");
        await AssertProblemAsync(freshTokenResponse, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_cannot_change_admin_status_and_missing_user_returns_not_found()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var caller = await CreateUserAsync(factory, RoleNames.Admin);
        var targetAdmin = await CreateUserAsync(factory, RoleNames.Admin);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", caller.Token);

        using var protectedResponse = await client.PutAsJsonAsync(
            $"/api/admin/users/{targetAdmin.User.Id:D}/status", new { isActive = false });
        await AssertProblemAsync(protectedResponse, HttpStatusCode.Conflict);
        await using (var db = fixture.CreateDbContext())
        {
            Assert.True(await db.Users.Where(user => user.Id == targetAdmin.User.Id).Select(user => user.IsActive).SingleAsync());
            Assert.Null(await db.AuthSessions.Where(session => session.Id == targetAdmin.SessionId)
                .Select(session => session.RevokedAtUtc).SingleAsync());
        }

        using var missingResponse = await client.PutAsJsonAsync(
            $"/api/admin/users/{Guid.NewGuid():D}/status", new { isActive = false });
        await AssertProblemAsync(missingResponse, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Status_updates_are_idempotent_without_reopening_revoked_sessions()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var admin = await CreateUserAsync(factory, RoleNames.Admin);
        var customer = await CreateUserAsync(factory, RoleNames.Customer);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.Token);

        using (var alreadyActive = await client.PutAsJsonAsync(
                   $"/api/admin/users/{customer.User.Id:D}/status", new { isActive = true }))
        {
            Assert.Equal(HttpStatusCode.NoContent, alreadyActive.StatusCode);
        }

        await using (var db = fixture.CreateDbContext())
        {
            Assert.Null(await db.AuthSessions.Where(session => session.Id == customer.SessionId)
                .Select(session => session.RevokedAtUtc).SingleAsync());
        }

        using (var deactivate = await client.PutAsJsonAsync(
                   $"/api/admin/users/{customer.User.Id:D}/status", new { isActive = false }))
        {
            Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);
        }

        var lateSessionId = await CreateSessionAsync(customer.User.Id);
        using var repeatDeactivate = await client.PutAsJsonAsync(
            $"/api/admin/users/{customer.User.Id:D}/status", new { isActive = false });
        Assert.Equal(HttpStatusCode.NoContent, repeatDeactivate.StatusCode);
        await using var checkDb = fixture.CreateDbContext();
        Assert.NotNull(await checkDb.AuthSessions.Where(session => session.Id == lateSessionId)
            .Select(session => session.RevokedAtUtc).SingleAsync());
    }

    [Fact]
    public async Task Missing_or_null_is_active_is_rejected_without_changing_the_user()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var admin = await CreateUserAsync(factory, RoleNames.Admin);
        var customer = await CreateUserAsync(factory, RoleNames.Customer);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.Token);

        using (var missing = await client.PutAsJsonAsync($"/api/admin/users/{customer.User.Id:D}/status", new { }))
        {
            await AssertProblemAsync(missing, HttpStatusCode.BadRequest);
        }

        using (var nullValue = await client.PutAsJsonAsync(
                   $"/api/admin/users/{customer.User.Id:D}/status", new { isActive = (bool?)null }))
        {
            await AssertProblemAsync(nullValue, HttpStatusCode.BadRequest);
        }

        await using var db = fixture.CreateDbContext();
        Assert.True(await db.Users.Where(user => user.Id == customer.User.Id).Select(user => user.IsActive).SingleAsync());
        Assert.Null(await db.AuthSessions.Where(session => session.Id == customer.SessionId)
            .Select(session => session.RevokedAtUtc).SingleAsync());
    }

    private ApiWebApplicationFactory CreateFactory() => new(connectionString: fixture.ConnectionString);

    private static async Task<TestUser> CreateUserAsync(
        ApiWebApplicationFactory factory,
        string role,
        int sessionCount = 1,
        DateTimeOffset? lockoutEnd = null,
        int accessFailedCount = 0)
    {
        var email = $"admin-status-{Guid.NewGuid():N}@example.test";
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = $"Admin status {role} user",
            CreatedAtUtc = DateTime.UtcNow,
            LockoutEnd = lockoutEnd,
            AccessFailedCount = accessFailedCount
        };
        var createResult = await userManager.CreateAsync(user);
        Assert.True(createResult.Succeeded, string.Join("; ", createResult.Errors.Select(error => error.Description)));
        var roleResult = await userManager.AddToRoleAsync(user, role);
        Assert.True(roleResult.Succeeded, string.Join("; ", roleResult.Errors.Select(error => error.Description)));

        var sessions = new Guid[sessionCount];
        var dbContext = scope.ServiceProvider.GetRequiredService<LumiCineDbContext>();
        var now = DateTime.UtcNow;
        for (var index = 0; index < sessions.Length; index++)
        {
            sessions[index] = Guid.NewGuid();
            dbContext.AuthSessions.Add(new AuthSession
            {
                Id = sessions[index],
                UserId = user.Id,
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddDays(7)
            });
        }

        await dbContext.SaveChangesAsync();
        var token = factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(user.Id, sessions[0], [role]).Value;
        return new TestUser(user, sessions[0], token);
    }

    private async Task<Guid> CreateSessionAsync(Guid userId)
    {
        var sessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var db = fixture.CreateDbContext();
        db.AuthSessions.Add(new AuthSession
        {
            Id = sessionId,
            UserId = userId,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddDays(7)
        });
        await db.SaveChangesAsync();
        return sessionId;
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expectedStatus)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal((int)expectedStatus, body.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("traceId").GetString()));
    }

    private sealed record TestUser(ApplicationUser User, Guid SessionId, string Token);
}
