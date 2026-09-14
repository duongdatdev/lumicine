using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LumiCine.IntegrationTests.Persistence;

namespace LumiCine.IntegrationTests.Authentication;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class CsrfEndpointTests(PostgresDatabaseFixture fixture)
{
    [Fact]
    public async Task Csrf_endpoint_sets_secure_cookie_and_returns_uncached_request_token()
    {
        using var factory = CreateFactory(includeTestControllers: true);
        using var client = CreateHttpsClient(factory);

        using var response = await client.GetAsync("/api/auth/csrf");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("__Host-LumiCine.Antiforgery=", cookie, StringComparison.Ordinal);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        var body = await response.Content.ReadFromJsonAsync<CsrfTokenResponse>();
        Assert.False(string.IsNullOrWhiteSpace(body?.RequestToken));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid-token")]
    public async Task Csrf_protected_post_rejects_missing_or_invalid_request_token(string? requestToken)
    {
        using var factory = CreateFactory(includeTestControllers: true);
        using var client = CreateHttpsClient(factory);
        using var tokenResponse = await client.GetAsync("/api/auth/csrf");
        tokenResponse.EnsureSuccessStatusCode();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/_test/auth/csrf-probe");
        if (requestToken is not null)
        {
            request.Headers.Add("X-CSRF-TOKEN", requestToken);
        }

        using var response = await client.SendAsync(request);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Csrf_protected_post_accepts_matching_cookie_and_request_token()
    {
        using var factory = CreateFactory(includeTestControllers: true);
        using var client = CreateHttpsClient(factory);
        using var tokenResponse = await client.GetAsync("/api/auth/csrf");
        var token = (await tokenResponse.Content.ReadFromJsonAsync<CsrfTokenResponse>())!.RequestToken;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/_test/auth/csrf-probe");
        request.Headers.Add("X-CSRF-TOKEN", token);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Test_only_csrf_probe_is_not_mapped_in_normal_host()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/_test/auth/csrf-probe", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Csrf_endpoint_rejects_http_with_problem_details()
    {
        using var factory = CreateFactory(includeTestControllers: true);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/auth/csrf");

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "HTTPS required");
    }

    [Fact]
    public async Task Csrf_filter_rejects_http_even_when_client_manually_sends_valid_cookie_and_token()
    {
        using var factory = CreateFactory(includeTestControllers: true);
        using var httpsClient = CreateHttpsClient(factory);
        using var tokenResponse = await httpsClient.GetAsync("/api/auth/csrf");
        var token = (await tokenResponse.Content.ReadFromJsonAsync<CsrfTokenResponse>())!.RequestToken;
        var cookie = tokenResponse.Headers.GetValues("Set-Cookie").Single().Split(';', 2)[0];
        using var httpClient = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/_test/auth/csrf-probe");
        request.Headers.Add("Cookie", cookie);
        request.Headers.Add("X-CSRF-TOKEN", token);

        using var response = await httpClient.SendAsync(request);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "HTTPS required");
    }

    private ApiWebApplicationFactory CreateFactory(bool includeTestControllers = false) =>
        new(connectionString: fixture.ConnectionString, includeTestControllers: includeTestControllers);

    private static HttpClient CreateHttpsClient(ApiWebApplicationFactory factory) =>
        factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode statusCode, string title = "Invalid CSRF token")
    {
        Assert.Equal(statusCode, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal((int)statusCode, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(title, body.RootElement.GetProperty("title").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("traceId").GetString()));
    }

    private sealed record CsrfTokenResponse(string RequestToken);
}
