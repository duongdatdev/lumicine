using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace LumiCine.IntegrationTests;

[Collection(Persistence.PostgresDatabaseCollection.Name)]
public sealed class ReadyEndpointTests(Persistence.PostgresDatabaseFixture fixture)
{
    [Fact]
    public async Task Ready_returns_healthy_when_database_is_connected()
    {
        using var factory = CreateFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready");
        var payload = await response.Content.ReadFromJsonAsync<HealthPayload>();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", payload?.Status);
    }

    [Fact]
    public async Task Ready_returns_generic_unhealthy_and_live_stays_available_when_database_is_unreachable()
    {
        const string connectionString = "Host=127.0.0.1;Port=1;Database=unreachable;Username=test;Timeout=1;Pooling=false";
        using var factory = CreateFactory(connectionString);
        using var client = factory.CreateClient();

        using var readyResponse = await client.GetAsync("/health/ready");
        var body = await readyResponse.Content.ReadAsStringAsync();
        using var liveResponse = await client.GetAsync("/health/live");

        Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, readyResponse.StatusCode);
        Assert.Contains("Unhealthy", body);
        Assert.DoesNotContain("Password", body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(System.Net.HttpStatusCode.OK, liveResponse.StatusCode);
    }

    [Fact]
    public async Task Ready_returns_generic_unhealthy_and_live_stays_available_without_connection_string()
    {
        using var factory = CreateFactory(string.Empty);
        using var client = factory.CreateClient();

        using var readyResponse = await client.GetAsync("/health/ready");
        var body = await readyResponse.Content.ReadAsStringAsync();
        using var liveResponse = await client.GetAsync("/health/live");

        Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, readyResponse.StatusCode);
        Assert.Contains("Unhealthy", body);
        Assert.DoesNotContain("Password", body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(System.Net.HttpStatusCode.OK, liveResponse.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory(string connectionString)
    {
        var factory = new ApiWebApplicationFactory("Development");
        return factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:LumiCine"] = connectionString
                })));
    }

    private sealed record HealthPayload(string Status);
}