using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LumiCine.Application.Accounts;
using LumiCine.Infrastructure.Authentication;
using LumiCine.Infrastructure.Identity;
using LumiCine.Infrastructure.Persistence;
using LumiCine.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace LumiCine.IntegrationTests.Authentication;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class JwtBearerEndpointTests(PostgresDatabaseFixture fixture)
{
    [Fact]
    public async Task Missing_token_returns_problem_details_and_bearer_challenge()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/_test/auth/authenticated");

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "Unauthorized");
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("wrong-signature")]
    [InlineData("wrong-issuer")]
    [InlineData("wrong-audience")]
    [InlineData("unsupported-algorithm")]
    public async Task Invalid_jwt_is_rejected_with_generic_problem_details(string invalidKind)
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var user = await CreateUserAsync();
        var sessionId = Guid.NewGuid();
        await CreateSessionAsync(user, sessionId);
        var token = CreateToken(user.Id.ToString("D"), sessionId.ToString("D"), GetSigningCredentials(factory), invalidKind);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync("/_test/auth/authenticated");

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "Unauthorized");
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
    }

    [Theory]
    [InlineData("bad-sub")]
    [InlineData("bad-sid")]
    [InlineData("missing-session")]
    [InlineData("revoked-session")]
    [InlineData("expired-session")]
    [InlineData("inactive-user")]
    [InlineData("mismatched-user")]
    public async Task Jwt_is_rejected_when_subject_or_session_is_invalid(string invalidKind)
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var signingCredentials = GetSigningCredentials(factory);
        var sessionOwner = await CreateUserAsync();
        var tokenOwner = invalidKind == "mismatched-user" ? await CreateUserAsync() : sessionOwner;
        var sessionId = Guid.NewGuid();
        if (invalidKind != "missing-session")
        {
            await CreateSessionAsync(sessionOwner, sessionId,
                revoked: invalidKind == "revoked-session",
                expired: invalidKind == "expired-session",
                active: invalidKind != "inactive-user");
        }

        var token = invalidKind switch
        {
            "bad-sub" => CreateToken("not-a-guid", sessionId.ToString("D"), signingCredentials),
            "bad-sid" => CreateToken(tokenOwner.Id.ToString("D"), "not-a-guid", signingCredentials),
            _ => CreateToken(tokenOwner.Id.ToString("D"), sessionId.ToString("D"), signingCredentials)
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync("/_test/auth/authenticated");

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "Unauthorized");
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
    }

    [Fact]
    public async Task Valid_session_and_roles_authorize_customer_and_admin_routes()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var user = await CreateUserAsync();
        var sessionId = Guid.NewGuid();
        await CreateSessionAsync(user, sessionId);

        var tokenIssuer = factory.Services.GetRequiredService<IAccessTokenIssuer>();
        var customerToken = tokenIssuer.Issue(user.Id, sessionId, ["Customer"]).Value;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);
        using (var customerResponse = await client.GetAsync("/_test/auth/customer"))
        {
            Assert.Equal(HttpStatusCode.OK, customerResponse.StatusCode);
        }

        using (var adminResponse = await client.GetAsync("/_test/auth/admin"))
        {
            await AssertProblemAsync(adminResponse, HttpStatusCode.Forbidden, "Forbidden");
        }

        using var identityResponse = await client.GetAsync("/_test/auth/authenticated");
        Assert.Equal(HttpStatusCode.OK, identityResponse.StatusCode);
        using var identityBody = await JsonDocument.ParseAsync(await identityResponse.Content.ReadAsStreamAsync());
        Assert.Equal(user.Id.ToString("D"), identityBody.RootElement.GetProperty("userId").GetString());
        Assert.Equal(sessionId.ToString("D"), identityBody.RootElement.GetProperty("sessionId").GetString());

        var adminToken = tokenIssuer.Issue(user.Id, sessionId, ["Admin"]).Value;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var authorizedAdminResponse = await client.GetAsync("/_test/auth/admin");
        Assert.Equal(HttpStatusCode.OK, authorizedAdminResponse.StatusCode);
    }

    [Fact]
    public async Task Recreated_test_hosts_validate_tokens_after_prior_signing_keys_are_disposed()
    {
        for (var index = 0; index < 2; index++)
        {
            using var factory = CreateFactory();
            using var client = factory.CreateClient();
            var user = await CreateUserAsync();
            var sessionId = Guid.NewGuid();
            await CreateSessionAsync(user, sessionId);
            var token = factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(user.Id, sessionId, ["Customer"]).Value;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await client.GetAsync("/_test/auth/customer");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    private ApiWebApplicationFactory CreateFactory() => new(connectionString: fixture.ConnectionString, includeTestControllers: true);

    private async Task<ApplicationUser> CreateUserAsync(bool active = true)
    {
        await using var db = fixture.CreateDbContext();
        var email = $"{Guid.NewGuid():N}@example.com";
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            IsActive = active,
            DisplayName = "JWT test user"
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private async Task CreateSessionAsync(ApplicationUser user, Guid sessionId, bool revoked = false, bool expired = false, bool active = true)
    {
        if (!active && user.IsActive)
        {
            await using var userDb = fixture.CreateDbContext();
            var trackedUser = await userDb.Users.SingleAsync(item => item.Id == user.Id);
            trackedUser.IsActive = false;
            await userDb.SaveChangesAsync();
        }

        var now = DateTime.UtcNow;
        await using var db = fixture.CreateDbContext();
        db.AuthSessions.Add(new AuthSession
        {
            Id = sessionId,
            UserId = user.Id,
            CreatedAtUtc = expired ? now.AddDays(-2) : now,
            ExpiresAtUtc = expired ? now.AddDays(-1) : now.AddDays(7),
            RevokedAtUtc = revoked ? now : null
        });
        await db.SaveChangesAsync();
    }

    private static SigningCredentials GetSigningCredentials(ApiWebApplicationFactory factory) =>
        factory.Services.GetRequiredService<JwtSigningKey>().SigningCredentials;

    private static string CreateToken(
        string subject,
        string session,
        SigningCredentials baseSigningCredentials,
        string? invalidKind = null,
        string role = "Customer")
    {
        var now = DateTime.UtcNow;
        var issuedAt = invalidKind == "expired" ? now.AddMinutes(-2) : now;
        var expires = invalidKind == "expired" ? now.AddMinutes(-1) : now.AddMinutes(15);
        SigningCredentials signingCredentials;
        if (invalidKind == "wrong-signature")
        {
            using var wrongRsa = RSA.Create(2048);
            signingCredentials = new SigningCredentials(new RsaSecurityKey(wrongRsa.ExportParameters(true)), SecurityAlgorithms.RsaSha256);
        }
        else if (invalidKind == "unsupported-algorithm")
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("test-only-shared-secret-32-bytes-long"));
            signingCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        }
        else
        {
            signingCredentials = baseSigningCredentials;
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = invalidKind == "wrong-issuer" ? "Other" : "LumiCine",
            Audience = invalidKind == "wrong-audience" ? "Other.Web" : "LumiCine.Web",
            IssuedAt = issuedAt,
            NotBefore = issuedAt,
            Expires = expires,
            Subject = new ClaimsIdentity([
                new Claim(Microsoft.IdentityModel.JsonWebTokens.JwtRegisteredClaimNames.Sub, subject),
                new Claim(Microsoft.IdentityModel.JsonWebTokens.JwtRegisteredClaimNames.Sid, session),
                new Claim("role", role)
            ]),
            SigningCredentials = signingCredentials
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode statusCode, string title)
    {
        Assert.Equal(statusCode, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal((int)statusCode, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(title, body.RootElement.GetProperty("title").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("traceId").GetString()));
        Assert.DoesNotContain("Invalid token", body.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
