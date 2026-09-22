using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography;
using LumiCine.IntegrationTests.Authentication;

namespace LumiCine.IntegrationTests;

public sealed class ApiWebApplicationFactory(
    string environment = "Development",
    string? connectionString = null,
    bool includeTestControllers = false,
    int authRateLimitPermitLimit = 10000,
    int authRateLimitWindowSeconds = 60) : WebApplicationFactory<Program>
{
    private static readonly string TestJwtPrivateKeyPem = CreateJwtTestPrivateKeyPem();
    internal static string JwtTestPrivateKeyPem => TestJwtPrivateKeyPem;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseContentRoot(FindApiContentRoot());
        builder.UseEnvironment(environment);
        var jwtTestSettings = new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "LumiCine",
            ["Jwt:Audience"] = "LumiCine.Web",
            ["Jwt:PrivateKeyPem"] = TestJwtPrivateKeyPem,
            ["AuthRateLimit:PermitLimit"] = authRateLimitPermitLimit.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["AuthRateLimit:WindowSeconds"] = authRateLimitWindowSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        if (connectionString is not null)
        {
            jwtTestSettings["ConnectionStrings:LumiCine"] = connectionString;
        }
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(jwtTestSettings));
        if (includeTestControllers)
        {
            builder.ConfigureTestServices(services =>
                services.AddControllers().AddApplicationPart(typeof(ProtectedProbeController).Assembly));
        }
    }

    private static string CreateJwtTestPrivateKeyPem()
    {
        using var rsa = RSA.Create(2048);
        return rsa.ExportPkcs8PrivateKeyPem();
    }

    private static string FindApiContentRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LumiCine.slnx")))
        {
            directory = directory.Parent;
        }

        return directory is null
            ? throw new DirectoryNotFoundException("Could not find LumiCine.slnx from the test output directory.")
            : Path.Combine(directory.FullName, "backend", "LumiCine.API");
    }
}
