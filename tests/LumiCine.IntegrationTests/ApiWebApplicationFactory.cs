using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;

namespace LumiCine.IntegrationTests;

public sealed class ApiWebApplicationFactory(string environment = "Development") : WebApplicationFactory<Program>
{
    private static readonly string JwtTestPrivateKeyPem = CreateJwtTestPrivateKeyPem();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseContentRoot(FindApiContentRoot());
        builder.UseEnvironment(environment);
        var jwtTestSettings = new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "LumiCine",
            ["Jwt:Audience"] = "LumiCine.Web",
            ["Jwt:PrivateKeyPem"] = JwtTestPrivateKeyPem
        };
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(jwtTestSettings));
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
