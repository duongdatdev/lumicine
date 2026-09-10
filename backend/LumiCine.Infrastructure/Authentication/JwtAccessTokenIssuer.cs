using System.Security.Claims;
using System.Security.Cryptography;
using LumiCine.Application.Accounts;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace LumiCine.Infrastructure.Authentication;

public sealed class JwtAccessTokenIssuer : IAccessTokenIssuer, IDisposable
{
    private static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(15);
    private readonly RSA _rsa;
    private readonly SigningCredentials _signingCredentials;
    private readonly JwtOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly JsonWebTokenHandler _handler = new();

    public JwtAccessTokenIssuer(IOptions<JwtOptions> options, TimeProvider timeProvider)
    {
        _options = options.Value;
        _timeProvider = timeProvider;
        _rsa = RSA.Create();
        _rsa.ImportFromPem(_options.PrivateKeyPem);
        _signingCredentials = new SigningCredentials(new RsaSecurityKey(_rsa), SecurityAlgorithms.RsaSha256);
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
            SigningCredentials = _signingCredentials
        });

        return new IssuedAccessToken(token, (int)AccessTokenLifetime.TotalSeconds);
    }

    public void Dispose() => _rsa.Dispose();
}
