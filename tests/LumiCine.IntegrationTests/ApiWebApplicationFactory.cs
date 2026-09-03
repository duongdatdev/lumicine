using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LumiCine.IntegrationTests;

public sealed class ApiWebApplicationFactory(string environment = "Development") : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseContentRoot(FindApiContentRoot());
        builder.UseEnvironment(environment);
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
