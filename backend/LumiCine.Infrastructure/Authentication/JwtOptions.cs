using System.Security.Cryptography;

namespace LumiCine.Infrastructure.Authentication;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string PrivateKeyPem { get; set; } = string.Empty;

    internal bool IsValid()
    {
        if (string.IsNullOrWhiteSpace(Issuer) || string.IsNullOrWhiteSpace(Audience) || string.IsNullOrWhiteSpace(PrivateKeyPem) ||
            !PrivateKeyPem.Contains("-----BEGIN PRIVATE KEY-----", StringComparison.Ordinal) ||
            !PrivateKeyPem.Contains("-----END PRIVATE KEY-----", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(PrivateKeyPem);
            return rsa.KeySize >= 2048 && rsa.ExportParameters(true).D is { Length: > 0 };
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
