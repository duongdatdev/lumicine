using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LumiCine.IntegrationTests;

public sealed class ValidationEndpointTests
{
    private const string Route = "/__test/validation-probe";

    [Fact]
    public async Task Valid_request_succeeds()
    {
        using var factory = CreateProbeFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(Route, new { name = "Film", quantity = 2 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_required_and_range_values_return_problem_details_with_trace_id()
    {
        using var factory = CreateProbeFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(Route, new { name = "", quantity = 11 });
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = document.RootElement;
        var errors = root.GetProperty("errors");
        var errorKeys = errors.EnumerateObject().Select(error => error.Name).ToArray();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains(nameof(ValidationProbeRequest.Name), errorKeys);
        Assert.Contains(nameof(ValidationProbeRequest.Quantity), errorKeys);
        Assert.True(root.TryGetProperty("traceId", out var traceId));
        Assert.False(string.IsNullOrWhiteSpace(traceId.GetString()));
    }

    [Fact]
    public async Task Malformed_json_returns_problem_details_with_trace_id()
    {
        using var factory = CreateProbeFactory();
        using var client = factory.CreateClient();
        using var content = new StringContent("{", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(Route, content);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = document.RootElement;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(root.GetProperty("errors").EnumerateObject().Any());
        Assert.True(root.TryGetProperty("traceId", out var traceId));
        Assert.False(string.IsNullOrWhiteSpace(traceId.GetString()));
    }

    [Fact]
    public async Task Probe_route_is_not_part_of_the_normal_host()
    {
        using var factory = new ApiWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(Route, new { name = "Film", quantity = 2 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateProbeFactory()
    {
        var factory = new ApiWebApplicationFactory();
        return factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddControllers().AddApplicationPart(typeof(ValidationProbeController).Assembly)));
    }
}