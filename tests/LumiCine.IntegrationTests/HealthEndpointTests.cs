using System.Net.Http.Json;

namespace LumiCine.IntegrationTests;

public sealed class HealthEndpointTests
{
    [Fact]
    public async Task Live_returns_healthy()
    {
        using var factory = new ApiWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live");
        var payload = await response.Content.ReadFromJsonAsync<HealthPayload>();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", payload?.Status);
    }

    private sealed record HealthPayload(string Status);
}
