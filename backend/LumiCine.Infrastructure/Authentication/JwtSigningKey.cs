using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace LumiCine.Infrastructure.Authentication;

public sealed class JwtSigningKey : IDisposable
{
    private readonly RSA _privateRsa;
    private readonly RSA _publicRsa;

    public JwtSigningKey(IOptions<JwtOptions> options)
    {
        _privateRsa = RSA.Create();
        _privateRsa.ImportFromPem(options.Value.PrivateKeyPem);
        _publicRsa = RSA.Create();
        _publicRsa.ImportParameters(_privateRsa.ExportParameters(false));
        SigningCredentials = new SigningCredentials(
            new RsaSecurityKey(_privateRsa)
            {
                CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
            },
            SecurityAlgorithms.RsaSha256);
        ValidationKey = new RsaSecurityKey(_publicRsa)
        {
            CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
        };
    }

    public SigningCredentials SigningCredentials { get; }
    public SecurityKey ValidationKey { get; }

    public void Dispose()
    {
        _privateRsa.Dispose();
        _publicRsa.Dispose();
    }
}
