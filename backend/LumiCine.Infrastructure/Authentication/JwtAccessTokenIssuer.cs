using System.Security.Claims;
using LumiCine.Application.Accounts;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace LumiCine.Infrastructure.Authentication;

public sealed class JwtAccessTokenIssuer : IAccessTokenIssuer
{
    private static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(15);
    private readonly JwtSigningKey _signingKey;
    private readonly JwtOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly JsonWebTokenHandler _handler = new();

    public JwtAccessTokenIssuer(IOptions<JwtOptions> options, TimeProvider timeProvider, JwtSigningKey signingKey)
    {
        _options = options.Value;
        _timeProvider = timeProvider;
        _signingKey = signingKey;
    }

    public IssuedAccessToken Issue(Guid userId, Guid sessionId, IReadOnlyCollection<string> roles)
    {
        var issuedAt = _timeProvider.GetUtcNow();
        var expiresAt = issuedAt.Add(AccessTokenLifetime);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString("D")),
            new(JwtRegisteredClaimNames.Sid, sessionId.ToString("D")),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(JwtRegisteredClaimNames.Iat, issuedAt.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };
        claims.AddRange(roles.Select(role => new Claim("role", role)));

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = issuedAt.UtcDateTime,
            NotBefore = issuedAt.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Subject = new ClaimsIdentity(claims),
            SigningCredentials = _signingKey.SigningCredentials
        });

        return new IssuedAccessToken(token, (int)AccessTokenLifetime.TotalSeconds);
    }

}
