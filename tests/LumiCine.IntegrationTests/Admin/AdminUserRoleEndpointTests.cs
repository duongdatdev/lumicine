using System.Data.Common;
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
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace LumiCine.IntegrationTests.Admin;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class AdminUserRoleEndpointTests(PostgresDatabaseFixture fixture)
{
    [Theory]
    [InlineData(RoleNames.Customer, RoleNames.Staff)]
    [InlineData(RoleNames.Staff, RoleNames.Customer)]
    public async Task Role_switch_revokes_old_sessions_and_allows_a_fresh_session(string currentRole, string newRole)
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var admin = await CreateUserAsync(factory, RoleNames.Admin);
        var target = await CreateUserAsync(factory, currentRole, sessionCount: 2);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.Token);

        using var response = await client.PutAsJsonAsync(
            $"/api/admin/users/{target.User.Id:D}/role", new { role = newRole });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using (var db = fixture.CreateDbContext())
        {
            var roles = await (from userRole in db.UserRoles
                               join role in db.Roles on userRole.RoleId equals role.Id
                               where userRole.UserId == target.User.Id
                               select role.Name).ToArrayAsync();
            Assert.Equal([newRole], roles);
            var sessions = await db.AuthSessions.AsNoTracking()
                .Where(session => session.UserId == target.User.Id)
                .ToArrayAsync();
            Assert.Equal(2, sessions.Length);
            Assert.All(sessions, session => Assert.NotNull(session.RevokedAtUtc));
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);
        using (var oldTokenResponse = await client.GetAsync("/api/admin/users"))
        {
            await AssertProblemAsync(oldTokenResponse, HttpStatusCode.Unauthorized);
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.Token);
        using (var detailResponse = await client.GetAsync($"/api/admin/users/{target.User.Id:D}"))
        {
            Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
            using var detail = await JsonDocument.ParseAsync(await detailResponse.Content.ReadAsStreamAsync());
            Assert.Equal([newRole], detail.RootElement.GetProperty("roles").EnumerateArray().Select(role => role.GetString()!).ToArray());
        }

        var newSessionId = await CreateSessionAsync(target.User.Id);
        var freshToken = factory.Services.GetRequiredService<IAccessTokenIssuer>()
            .Issue(target.User.Id, newSessionId, [newRole]).Value;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", freshToken);
        using var freshTokenResponse = await client.GetAsync("/api/admin/users");
        await AssertProblemAsync(freshTokenResponse, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Same_role_is_idempotent_and_preserves_valid_sessions()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var admin = await CreateUserAsync(factory, RoleNames.Admin);
        var staff = await CreateUserAsync(factory, RoleNames.Staff);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.Token);

        using var response = await client.PutAsJsonAsync($"/api/admin/users/{staff.User.Id:D}/role", new { role = RoleNames.Staff });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var db = fixture.CreateDbContext();
        Assert.Null(await db.AuthSessions.Where(session => session.Id == staff.SessionId)
            .Select(session => session.RevokedAtUtc).SingleAsync());
        Assert.Equal(RoleNames.Staff, await (from userRole in db.UserRoles
                                             join role in db.Roles on userRole.RoleId equals role.Id
                                             where userRole.UserId == staff.User.Id
                                             select role.Name).SingleAsync());
    }

    [Fact]
    public async Task Admin_role_cannot_be_granted_or_modified()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var caller = await CreateUserAsync(factory, RoleNames.Admin);
        var customer = await CreateUserAsync(factory, RoleNames.Customer);
        var targetAdmin = await CreateUserAsync(factory, RoleNames.Admin);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", caller.Token);

        using (var grantAdmin = await client.PutAsJsonAsync(
                   $"/api/admin/users/{customer.User.Id:D}/role", new { role = RoleNames.Admin }))
        {
            await AssertProblemAsync(grantAdmin, HttpStatusCode.BadRequest);
        }

        using (var modifyAdmin = await client.PutAsJsonAsync(
                   $"/api/admin/users/{targetAdmin.User.Id:D}/role", new { role = RoleNames.Staff }))
        {
            await AssertProblemAsync(modifyAdmin, HttpStatusCode.Conflict);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var adminUserService = scope.ServiceProvider.GetRequiredService<IAdminUserService>();
            Assert.Equal(AdminUserStatusResult.InvalidRole, await adminUserService.SetRoleAsync(customer.User.Id, RoleNames.Admin));
        }

        await using var db = fixture.CreateDbContext();
        Assert.Equal(RoleNames.Customer, await GetSingleRoleAsync(db, customer.User.Id));
        Assert.Equal(RoleNames.Admin, await GetSingleRoleAsync(db, targetAdmin.User.Id));
        Assert.Null(await db.AuthSessions.Where(session => session.Id == customer.SessionId)
            .Select(session => session.RevokedAtUtc).SingleAsync());
        Assert.Null(await db.AuthSessions.Where(session => session.Id == targetAdmin.SessionId)
            .Select(session => session.RevokedAtUtc).SingleAsync());
    }

    [Fact]
    public async Task Only_admin_callers_can_change_roles()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var customer = await CreateUserAsync(factory, RoleNames.Customer);
        var staff = await CreateUserAsync(factory, RoleNames.Staff);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", customer.Token);
        using (var customerResponse = await client.PutAsJsonAsync(
                   $"/api/admin/users/{staff.User.Id:D}/role", new { role = RoleNames.Customer }))
        {
            await AssertProblemAsync(customerResponse, HttpStatusCode.Forbidden);
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staff.Token);
        using (var staffResponse = await client.PutAsJsonAsync(
                   $"/api/admin/users/{customer.User.Id:D}/role", new { role = RoleNames.Staff }))
        {
            await AssertProblemAsync(staffResponse, HttpStatusCode.Forbidden);
        }

        await using var db = fixture.CreateDbContext();
        Assert.Equal(RoleNames.Customer, await GetSingleRoleAsync(db, customer.User.Id));
        Assert.Equal(RoleNames.Staff, await GetSingleRoleAsync(db, staff.User.Id));
    }

    [Fact]
    public async Task Invalid_missing_or_null_role_is_rejected_without_changing_roles_or_sessions()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var admin = await CreateUserAsync(factory, RoleNames.Admin);
        var customer = await CreateUserAsync(factory, RoleNames.Customer);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.Token);

        foreach (var body in new object[]
                 {
                     new { },
                     new { role = (string?)null },
                     new { role = "" },
                     new { role = "customer" },
                     new { role = " Staff " }
                 })
        {
            using var response = await client.PutAsJsonAsync($"/api/admin/users/{customer.User.Id:D}/role", body);
            await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        }

        await using var db = fixture.CreateDbContext();
        Assert.Equal(RoleNames.Customer, await GetSingleRoleAsync(db, customer.User.Id));
        Assert.Null(await db.AuthSessions.Where(session => session.Id == customer.SessionId)
            .Select(session => session.RevokedAtUtc).SingleAsync());
    }

    [Fact]
    public async Task Missing_user_returns_not_found()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var admin = await CreateUserAsync(factory, RoleNames.Admin);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.Token);

        using var response = await client.PutAsJsonAsync($"/api/admin/users/{Guid.NewGuid():D}/role", new { role = RoleNames.Staff });

        await AssertProblemAsync(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Failed_role_insert_rolls_back_role_removal_and_session_revocation()
    {
        using var factory = CreateFactory();
        var admin = await CreateUserAsync(factory, RoleNames.Admin);
        var customer = await CreateUserAsync(factory, RoleNames.Customer, sessionCount: 2);
        var interceptor = new FailOnUserRoleInsertInterceptor();
        using var failingFactory = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddDbContext<LumiCineDbContext>((_, options) => options.AddInterceptors(interceptor))));
        using var client = failingFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.Token);
        interceptor.Enabled = true;

        using var response = await client.PutAsJsonAsync($"/api/admin/users/{customer.User.Id:D}/role", new { role = RoleNames.Staff });

        await AssertProblemAsync(response, HttpStatusCode.InternalServerError);
        interceptor.Enabled = false;
        await using var db = fixture.CreateDbContext();
        Assert.Equal(RoleNames.Customer, await GetSingleRoleAsync(db, customer.User.Id));
        var sessions = await db.AuthSessions.AsNoTracking().Where(session => session.UserId == customer.User.Id).ToArrayAsync();
        Assert.Equal(2, sessions.Length);
        Assert.All(sessions, session => Assert.Null(session.RevokedAtUtc));
    }

    [Fact]
    public async Task Role_change_waits_for_an_existing_user_row_lock()
    {
        using var factory = CreateFactory();
        var customer = await CreateUserAsync(factory, RoleNames.Customer, sessionCount: 2);
        await using var lockDb = fixture.CreateDbContext();
        await using var transaction = await lockDb.Database.BeginTransactionAsync();
        await lockDb.Users
            .FromSqlInterpolated($"SELECT * FROM \"Users\" WHERE \"Id\" = {customer.User.Id} FOR UPDATE")
            .SingleAsync();

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAdminUserService>();
        var roleChange = service.SetRoleAsync(customer.User.Id, RoleNames.Staff);
        Assert.NotSame(roleChange, await Task.WhenAny(roleChange, Task.Delay(TimeSpan.FromMilliseconds(200))));

        await transaction.CommitAsync();
        Assert.Equal(AdminUserStatusResult.Success, await roleChange.WaitAsync(TimeSpan.FromSeconds(5)));

        await using var verifyDb = fixture.CreateDbContext();
        Assert.Equal(RoleNames.Staff, await GetSingleRoleAsync(verifyDb, customer.User.Id));
        var sessions = await verifyDb.AuthSessions.AsNoTracking()
            .Where(session => session.UserId == customer.User.Id)
            .ToArrayAsync();
        Assert.Equal(2, sessions.Length);
        Assert.All(sessions, session => Assert.NotNull(session.RevokedAtUtc));
    }

    private ApiWebApplicationFactory CreateFactory() => new(connectionString: fixture.ConnectionString);

    private static async Task<TestUser> CreateUserAsync(ApiWebApplicationFactory factory, string role, int sessionCount = 1)
    {
        var email = $"admin-role-{Guid.NewGuid():N}@example.test";
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = $"Admin role {role} user",
            CreatedAtUtc = DateTime.UtcNow
        };
        var createResult = await userManager.CreateAsync(user);
        Assert.True(createResult.Succeeded, string.Join("; ", createResult.Errors.Select(error => error.Description)));
        var roleResult = await userManager.AddToRoleAsync(user, role);
        Assert.True(roleResult.Succeeded, string.Join("; ", roleResult.Errors.Select(error => error.Description)));

        var db = scope.ServiceProvider.GetRequiredService<LumiCineDbContext>();
        var now = DateTime.UtcNow;
        var sessions = new Guid[sessionCount];
        for (var index = 0; index < sessions.Length; index++)
        {
            sessions[index] = Guid.NewGuid();
            db.AuthSessions.Add(new AuthSession
            {
                Id = sessions[index],
                UserId = user.Id,
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddDays(7)
            });
        }

        await db.SaveChangesAsync();
        var token = factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(user.Id, sessions[0], [role]).Value;
        return new TestUser(user, sessions[0], token);
    }

    private async Task<Guid> CreateSessionAsync(Guid userId)
    {
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var db = fixture.CreateDbContext();
        db.AuthSessions.Add(new AuthSession { Id = id, UserId = userId, CreatedAtUtc = now, ExpiresAtUtc = now.AddDays(7) });
        await db.SaveChangesAsync();
        return id;
    }

    private static Task<string?> GetSingleRoleAsync(LumiCineDbContext db, Guid userId) =>
        (from userRole in db.UserRoles
         join role in db.Roles on userRole.RoleId equals role.Id
         where userRole.UserId == userId
         select role.Name).SingleAsync();

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expectedStatus)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal((int)expectedStatus, body.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("traceId").GetString()));
    }

    private sealed record TestUser(ApplicationUser User, Guid SessionId, string Token);

    private sealed class FailOnUserRoleInsertInterceptor : DbCommandInterceptor
    {
        public bool Enabled { get; set; }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            ThrowIfRoleInsert(command);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ThrowIfRoleInsert(command);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result)
        {
            ThrowIfRoleInsert(command);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            ThrowIfRoleInsert(command);
            return ValueTask.FromResult(result);
        }

        private void ThrowIfRoleInsert(DbCommand command)
        {
            if (Enabled && command.CommandText.Contains("INSERT INTO \"UserRoles\"", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Injected role insert failure.");
            }
        }
    }
}
