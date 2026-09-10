using System.Security.Cryptography;
using LumiCine.Application.Accounts;
using LumiCine.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace LumiCine.IntegrationTests.Authentication;

public sealed class JwtAccessTokenIssuerTests
{
    [Fact]
    public async Task Issues_RS256_token_with_expected_issuer_audience_claims_and_lifetime()
    {
        using var signingRsa = RSA.Create(2048);
        var privateKeyPem = signingRsa.ExportPkcs8PrivateKeyPem();
        using var publicRsa = RSA.Create();
        publicRsa.ImportParameters(signingRsa.ExportParameters(false));
        var issuedAt = DateTimeOffset.UtcNow;
        using var serviceProvider = BuildServiceProvider(privateKeyPem, new FixedTimeProvider(issuedAt));
        var issuer = serviceProvider.GetRequiredService<IAccessTokenIssuer>();
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var issued = issuer.Issue(userId, sessionId, ["Customer", "Admin"]);
        var handler = new JsonWebTokenHandler();
        var token = handler.ReadJsonWebToken(issued.Value);
        var validation = await handler.ValidateTokenAsync(issued.Value, new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new RsaSecurityKey(publicRsa),
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ValidateIssuer = true,
            ValidIssuer = "LumiCine",
            ValidateAudience = true,
            ValidAudience = "LumiCine.Web",
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        });

        Assert.True(validation.IsValid, validation.Exception?.Message);
        Assert.Equal(SecurityAlgorithms.RsaSha256, token.Alg);
        Assert.Equal("LumiCine", token.Issuer);
        Assert.Equal("LumiCine.Web", Assert.Single(token.Audiences));
        Assert.Equal(userId.ToString("D"), token.Subject);
        Assert.Equal(sessionId.ToString("D"), token.GetClaim(JwtRegisteredClaimNames.Sid).Value);
        Assert.Equal(["Admin", "Customer"], token.Claims.Where(claim => claim.Type == "role").Select(claim => claim.Value).Order().ToArray());

        var issuedAtSeconds = long.Parse(token.GetClaim(JwtRegisteredClaimNames.Iat).Value);
        var expiresAtSeconds = long.Parse(token.GetClaim(JwtRegisteredClaimNames.Exp).Value);
        Assert.Equal(issuedAt.ToUnixTimeSeconds(), issuedAtSeconds);
        Assert.Equal(900, expiresAtSeconds - issuedAtSeconds);
        Assert.Equal(900, issued.ExpiresInSeconds);
        Assert.DoesNotContain(privateKeyPem, issued.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_jwt_configuration_fails_without_exposing_private_key()
    {
        using var weakRsa = RSA.Create(1024);
        var privateKeyPem = weakRsa.ExportPkcs8PrivateKeyPem();
        AssertInvalidConfiguration(privateKeyPem);
        AssertInvalidConfiguration(null);
    }

    private static void AssertInvalidConfiguration(string? privateKeyPem)
    {
        using var serviceProvider = BuildServiceProvider(privateKeyPem, TimeProvider.System);
        var error = Assert.Throws<OptionsValidationException>(
            () => serviceProvider.GetRequiredService<IAccessTokenIssuer>());
        if (privateKeyPem is not null)
        {
            Assert.DoesNotContain(privateKeyPem, error.Message, StringComparison.Ordinal);
        }
    }

    private static ServiceProvider BuildServiceProvider(string? privateKeyPem, TimeProvider timeProvider)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "LumiCine",
                ["Jwt:Audience"] = "LumiCine.Web",
                ["Jwt:PrivateKeyPem"] = privateKeyPem
            })
            .Build();
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        services.AddSingleton(timeProvider);
        return services.BuildServiceProvider();
    }

    private sealed class FixedTimeProvider(DateTimeOffset currentTime) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => currentTime;
    }
}
