using System.Net;
using System.Text.Json;
using LumiCine.API.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace LumiCine.IntegrationTests;

public sealed class ExceptionHandlerTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Unexpected_exception_returns_generic_problem_and_logs_matching_trace_id(string environment)
    {
        var logs = new CapturingLoggerProvider();
        using var factory = CreateProbeFactory(environment, logs);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/__test/exception-probe/unexpected");
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var traceId = root.GetProperty("traceId").GetString();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("An unexpected error occurred.", root.GetProperty("title").GetString());
        Assert.False(string.IsNullOrWhiteSpace(traceId));
        Assert.DoesNotContain("sensitive-test-exception-message", body);
        Assert.DoesNotContain(nameof(InvalidOperationException), body);
        Assert.DoesNotContain("stackTrace", body, StringComparison.OrdinalIgnoreCase);

        var handlerLogs = logs.Entries
            .Where(entry => entry.Category == typeof(GlobalExceptionHandler).FullName && entry.Level == LogLevel.Error)
            .ToArray();
        var handlerLog = Assert.Single(handlerLogs);
        Assert.Contains(traceId!, handlerLog.Message);
    }

    [Fact]
    public async Task Bad_http_request_preserves_its_status_code_and_hides_details()
    {
        using var factory = CreateProbeFactory("Development", new CapturingLoggerProvider());
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/__test/exception-probe/bad-request");
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, document.RootElement.GetProperty("status").GetInt32());
        Assert.True(document.RootElement.TryGetProperty("traceId", out _));
        Assert.DoesNotContain("sensitive-test-bad-request-message", body);
    }

    [Theory]
    [InlineData("Development", "text/html")]
    [InlineData("Development", "application/xml")]
    [InlineData("Production", "text/html")]
    [InlineData("Production", "application/xml")]
    public async Task Unsupported_accept_still_returns_generic_problem_details(string environment, string accept)
    {
        using var factory = CreateProbeFactory(environment, new CapturingLoggerProvider());
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/__test/exception-probe/unexpected");
        request.Headers.Accept.ParseAdd(accept);

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("An unexpected error occurred.", root.GetProperty("title").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
        Assert.DoesNotContain("sensitive-test-exception-message", body);
        Assert.DoesNotContain(nameof(InvalidOperationException), body);
        Assert.DoesNotContain("stackTrace", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Exception_probe_route_is_not_part_of_the_normal_host()
    {
        using var factory = new ApiWebApplicationFactory("Development");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/__test/exception-probe/unexpected");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
    private static WebApplicationFactory<Program> CreateProbeFactory(
        string environment,
        ILoggerProvider loggerProvider)
    {
        var factory = new ApiWebApplicationFactory(environment);
        return factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
                services.AddControllers().AddApplicationPart(typeof(ExceptionProbeController).Assembly));
            builder.ConfigureLogging(logging => logging.AddProvider(loggerProvider));
        });
    }
}
