using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using LumiCine.API.Controllers;
using LumiCine.Application.Accounts;
using LumiCine.Infrastructure.Identity;
using LumiCine.Infrastructure.Persistence;
using LumiCine.IntegrationTests.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LumiCine.IntegrationTests.Accounts;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class CurrentUserProfileEndpointTests(PostgresDatabaseFixture fixture)
{
    [Fact]
    public async Task Profile_concurrency_conflict_returns_409_problem_details()
    {
        var userId = Guid.NewGuid();
        var service = new StubProfileService(new CurrentUserProfileUpdateResult(CurrentUserProfileUpdateStatus.ConcurrencyConflict));
        var controller = new CurrentUserProfileController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    TraceIdentifier = "profile-conflict-test",
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub, userId.ToString("D"))],
                        "test"))
                }
            }
        };

        var result = await controller.Update(new UpdateCurrentUserProfileRequest { DisplayName = "New Name" }, CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.Equal("application/problem+json", Assert.Single(conflict.ContentTypes));
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("Profile changed", problem.Title);
        Assert.Equal("profile-conflict-test", problem.Extensions["traceId"]);
    }

    [Fact]
    public async Task Profile_requires_bearer_token_for_get_and_update()
    {
        using var factory = CreateFactory();
        using var client = CreateHttpsClient(factory);
        using var get = await client.GetAsync("/api/users/me");
        using var put = await client.PutAsJsonAsync("/api/users/me", new { displayName = "New Name" });

        Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, put.StatusCode);
    }

    [Fact]
    public async Task Profile_reads_and_updates_only_authenticated_account_display_name()
    {
        using var factory = CreateFactory();
        var first = await CreateUserAsync(factory, $"profile-one-{Guid.NewGuid():N}@example.test", "First User", "Customer");
        var second = await CreateUserAsync(factory, $"profile-two-{Guid.NewGuid():N}@example.test", "Second User", "Admin");
        using var firstClient = CreateHttpsClient(factory);
        using var secondClient = CreateHttpsClient(factory);
        var firstToken = await LoginAsync(firstClient, first.Email!);
        var secondToken = await LoginAsync(secondClient, second.Email!);

        using var firstGet = await SendAuthorizedAsync(firstClient, HttpMethod.Get, "/api/users/me", firstToken);
        using var firstBody = await JsonDocument.ParseAsync(await firstGet.Content.ReadAsStreamAsync());
        Assert.Equal(first.Id, firstBody.RootElement.GetProperty("id").GetGuid());
        Assert.Equal(first.Email, firstBody.RootElement.GetProperty("email").GetString());
        Assert.Equal("First User", firstBody.RootElement.GetProperty("displayName").GetString());
        Assert.Equal("Customer", firstBody.RootElement.GetProperty("roles")[0].GetString());
        Assert.False(firstBody.RootElement.TryGetProperty("passwordHash", out _));

        using var updated = await SendAuthorizedAsync(firstClient, HttpMethod.Put, "/api/users/me", firstToken,
            new { displayName = "  New Display Name  ", id = second.Id, email = second.Email, roles = new[] { "Admin" }, isActive = false });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        using var updatedBody = await JsonDocument.ParseAsync(await updated.Content.ReadAsStreamAsync());
        Assert.Equal(first.Id, updatedBody.RootElement.GetProperty("id").GetGuid());
        Assert.Equal(first.Email, updatedBody.RootElement.GetProperty("email").GetString());
        Assert.Equal("New Display Name", updatedBody.RootElement.GetProperty("displayName").GetString());
        Assert.Equal("Customer", updatedBody.RootElement.GetProperty("roles")[0].GetString());

        using var secondGet = await SendAuthorizedAsync(secondClient, HttpMethod.Get, "/api/users/me", secondToken);
        using var secondBody = await JsonDocument.ParseAsync(await secondGet.Content.ReadAsStreamAsync());
        Assert.Equal(second.Id, secondBody.RootElement.GetProperty("id").GetGuid());
        Assert.Equal(second.Email, secondBody.RootElement.GetProperty("email").GetString());
        Assert.Equal("Second User", secondBody.RootElement.GetProperty("displayName").GetString());
        Assert.Equal("Admin", secondBody.RootElement.GetProperty("roles")[0].GetString());

        await using var db = fixture.CreateDbContext();
        var savedFirst = await db.Users.SingleAsync(user => user.Id == first.Id);
        var savedSecond = await db.Users.SingleAsync(user => user.Id == second.Id);
        Assert.Equal("New Display Name", savedFirst.DisplayName);
        Assert.Equal(first.Email, savedFirst.Email);
        Assert.True(savedFirst.IsActive);
        Assert.Equal("Second User", savedSecond.DisplayName);
        Assert.Equal(second.Email, savedSecond.Email);
        Assert.True(savedSecond.IsActive);
    }

    [Theory]
    [InlineData("")]
    [InlineData("     ")]
    [InlineData("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
    public async Task Profile_rejects_empty_or_oversized_display_name(string displayName)
    {
        using var factory = CreateFactory();
        var user = await CreateUserAsync(factory, $"profile-invalid-{Guid.NewGuid():N}@example.test", "Valid Name", "Customer");
        using var client = CreateHttpsClient(factory);
        var token = await LoginAsync(client, user.Email!);

        using var response = await SendAuthorizedAsync(client, HttpMethod.Put, "/api/users/me", token, new { displayName });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var db = fixture.CreateDbContext();
        Assert.Equal("Valid Name", (await db.Users.SingleAsync(item => item.Id == user.Id)).DisplayName);
    }

    [Fact]
    public async Task Profile_rejects_null_display_name()
    {
        using var factory = CreateFactory();
        var user = await CreateUserAsync(factory, $"profile-null-{Guid.NewGuid():N}@example.test", "Valid Name", "Customer");
        using var client = CreateHttpsClient(factory);
        var token = await LoginAsync(client, user.Email!);

        using var response = await SendAuthorizedAsync(client, HttpMethod.Put, "/api/users/me", token, new { displayName = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private ApiWebApplicationFactory CreateFactory() => new(connectionString: fixture.ConnectionString);

    private static HttpClient CreateHttpsClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    private async Task<ApplicationUser> CreateUserAsync(ApiWebApplicationFactory factory, string email, string displayName, string role)
    {
        using var scope = factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(), UserName = email, Email = email, EmailConfirmed = true,
            IsActive = true, DisplayName = displayName, LockoutEnabled = true
        };
        var created = await manager.CreateAsync(user, "SecurePass123");
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(error => error.Description)));
        var roleResult = await manager.AddToRoleAsync(user, role);
        Assert.True(roleResult.Succeeded);
        return user;
    }

    private static async Task<string> LoginAsync(HttpClient client, string email)
    {
        using var csrfResponse = await client.GetAsync("/api/auth/csrf");
        csrfResponse.EnsureSuccessStatusCode();
        using var csrfBody = await JsonDocument.ParseAsync(await csrfResponse.Content.ReadAsStreamAsync());
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email, password = "SecurePass123" })
        };
        request.Headers.Add("X-CSRF-TOKEN", csrfBody.RootElement.GetProperty("requestToken").GetString());
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return body.RootElement.GetProperty("accessToken").GetString()!;
    }

    private static async Task<HttpResponseMessage> SendAuthorizedAsync(HttpClient client, HttpMethod method, string path, string token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(request);
    }

    private sealed class StubProfileService(CurrentUserProfileUpdateResult updateResult) : ICurrentUserProfileService
    {
        public Task<CurrentUserProfile?> GetAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult<CurrentUserProfile?>(null);

        public Task<CurrentUserProfileUpdateResult> UpdateDisplayNameAsync(Guid userId, string displayName, CancellationToken cancellationToken = default) =>
            Task.FromResult(updateResult);
    }
}
