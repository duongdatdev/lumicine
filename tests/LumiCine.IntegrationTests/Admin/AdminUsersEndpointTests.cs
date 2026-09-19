using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using LumiCine.Application.Accounts;
using LumiCine.Infrastructure.Authentication;
using LumiCine.Infrastructure.Identity;
using LumiCine.IntegrationTests.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LumiCine.IntegrationTests.Admin;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class AdminUsersEndpointTests(PostgresDatabaseFixture fixture)
{
    [Fact]
    public async Task Admin_user_queries_require_admin_role_and_return_redacted_user_data()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var customer = await CreateUserAsync(factory, RoleNames.Customer);
        var staff = await CreateUserAsync(factory, RoleNames.Staff);
        var admin = await CreateUserAsync(factory, RoleNames.Admin);

        using (var anonymous = await client.GetAsync("/api/admin/users"))
        {
            await AssertProblemAsync(anonymous, HttpStatusCode.Unauthorized);
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", customer.Token);
        using (var customerResponse = await client.GetAsync("/api/admin/users"))
        {
            await AssertProblemAsync(customerResponse, HttpStatusCode.Forbidden);
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staff.Token);
        using (var staffResponse = await client.GetAsync("/api/admin/users"))
        {
            await AssertProblemAsync(staffResponse, HttpStatusCode.Forbidden);
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.Token);
        using var listResponse = await client.GetAsync("/api/admin/users?pageSize=100");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        using var list = await JsonDocument.ParseAsync(await listResponse.Content.ReadAsStreamAsync());
        var adminSummary = list.RootElement.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == admin.User.Id);
        Assert.Equal(admin.User.Email, adminSummary.GetProperty("email").GetString());
        Assert.Contains(RoleNames.Admin, adminSummary.GetProperty("roles").EnumerateArray().Select(role => role.GetString()));
        AssertRedacted(list.RootElement.ToString());

        using var detailResponse = await client.GetAsync($"/api/admin/users/{customer.User.Id:D}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        using var detail = await JsonDocument.ParseAsync(await detailResponse.Content.ReadAsStreamAsync());
        Assert.Equal(customer.User.Id, detail.RootElement.GetProperty("id").GetGuid());
        Assert.Equal(customer.User.Email, detail.RootElement.GetProperty("email").GetString());
        Assert.Contains(RoleNames.Customer, detail.RootElement.GetProperty("roles").EnumerateArray().Select(role => role.GetString()));
        AssertRedacted(detail.RootElement.ToString());

        using var missingResponse = await client.GetAsync($"/api/admin/users/{Guid.NewGuid():D}");
        await AssertProblemAsync(missingResponse, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task User_listing_paginates_stably_and_large_offsets_return_empty_pages()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var admin = await CreateUserAsync(factory, RoleNames.Admin);
        var newest = await CreateUserAsync(factory, RoleNames.Customer, DateTime.UtcNow.AddYears(1).AddSeconds(1));
        var next = await CreateUserAsync(factory, RoleNames.Staff, DateTime.UtcNow.AddYears(1));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.Token);

        using (var defaultResponse = await client.GetAsync("/api/admin/users"))
        {
            Assert.Equal(HttpStatusCode.OK, defaultResponse.StatusCode);
            using var defaultPage = await JsonDocument.ParseAsync(await defaultResponse.Content.ReadAsStreamAsync());
            Assert.Equal(1, defaultPage.RootElement.GetProperty("page").GetInt32());
            Assert.Equal(20, defaultPage.RootElement.GetProperty("pageSize").GetInt32());
        }

        using (var firstResponse = await client.GetAsync("/api/admin/users?page=1&pageSize=1"))
        {
            using var first = await JsonDocument.ParseAsync(await firstResponse.Content.ReadAsStreamAsync());
            Assert.Equal(newest.User.Id, Assert.Single(first.RootElement.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        }

        using (var secondResponse = await client.GetAsync("/api/admin/users?page=2&pageSize=1"))
        {
            using var second = await JsonDocument.ParseAsync(await secondResponse.Content.ReadAsStreamAsync());
            Assert.Equal(next.User.Id, Assert.Single(second.RootElement.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        }

        using var largeOffsetResponse = await client.GetAsync($"/api/admin/users?page={int.MaxValue}&pageSize=100");
        Assert.Equal(HttpStatusCode.OK, largeOffsetResponse.StatusCode);
        using var largeOffset = await JsonDocument.ParseAsync(await largeOffsetResponse.Content.ReadAsStreamAsync());
        Assert.Empty(largeOffset.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(int.MaxValue, largeOffset.RootElement.GetProperty("page").GetInt32());
    }

    [Fact]
    public async Task Invalid_paging_arguments_return_validation_problem_details()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var admin = await CreateUserAsync(factory, RoleNames.Admin);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.Token);

        foreach (var query in new[] { "?page=0", "?page=-1", "?pageSize=0", "?pageSize=101" })
        {
            using var response = await client.GetAsync($"/api/admin/users{query}");
            await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        }
    }

    private ApiWebApplicationFactory CreateFactory() => new(connectionString: fixture.ConnectionString);

    private static async Task<TestUser> CreateUserAsync(
        ApiWebApplicationFactory factory,
        string role,
        DateTime? createdAtUtc = null)
    {
        var email = $"admin-api-{Guid.NewGuid():N}@example.test";
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = $"Admin API {role} user",
            CreatedAtUtc = DateTime.UtcNow
        };
        var createResult = await userManager.CreateAsync(user);
        Assert.True(createResult.Succeeded, string.Join("; ", createResult.Errors.Select(error => error.Description)));
        var roleResult = await userManager.AddToRoleAsync(user, role);
        Assert.True(roleResult.Succeeded, string.Join("; ", roleResult.Errors.Select(error => error.Description)));

        if (createdAtUtc is not null)
        {
            user.CreatedAtUtc = createdAtUtc.Value;
            var updateResult = await userManager.UpdateAsync(user);
            Assert.True(updateResult.Succeeded, string.Join("; ", updateResult.Errors.Select(error => error.Description)));
        }

        var sessionId = Guid.NewGuid();
        var dbContext = scope.ServiceProvider.GetRequiredService<LumiCine.Infrastructure.Persistence.LumiCineDbContext>();
        var now = DateTime.UtcNow;
        dbContext.AuthSessions.Add(new AuthSession
        {
            Id = sessionId,
            UserId = user.Id,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddDays(7)
        });
        await dbContext.SaveChangesAsync();
        var accessToken = factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(user.Id, sessionId, [role]);
        return new TestUser(user, accessToken.Value);
    }

    private static void AssertRedacted(string json)
    {
        Assert.DoesNotContain("passwordhash", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("securitystamp", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expectedStatus)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal((int)expectedStatus, body.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("traceId").GetString()));
    }

    private sealed record TestUser(ApplicationUser User, string Token);
}
