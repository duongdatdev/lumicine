using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LumiCine.IntegrationTests.Authentication;

public sealed class AuthRateLimitTests
{
    [Fact]
    public async Task Auth_endpoint_returns_retryable_problem_and_csrf_remains_available()
    {
        using var factory = new ApiWebApplicationFactory(authRateLimitPermitLimit: 1);
        using var client = factory.CreateClient();
        client.BaseAddress = new Uri("https://localhost");

        using var first = await client.PostAsJsonAsync("/api/auth/login", new { email = "user@example.test", password = "wrong-password" });
        Assert.Equal(HttpStatusCode.BadRequest, first.StatusCode);
        using var rejected = await client.PostAsJsonAsync("/api/auth/register", new { email = "new@example.test", displayName = "New User", password = "SecurePass123" });

        Assert.Equal((HttpStatusCode)429, rejected.StatusCode);
        Assert.Equal("application/problem+json", rejected.Content.Headers.ContentType?.MediaType);
        Assert.True(rejected.Headers.TryGetValues("Retry-After", out var retryAfter));
        Assert.True(int.Parse(Assert.Single(retryAfter!)) > 0);
        using var body = await JsonDocument.ParseAsync(await rejected.Content.ReadAsStreamAsync());
        Assert.Equal(429, body.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("traceId").GetString()));

        using var csrf = await client.GetAsync("/api/auth/csrf");
        Assert.Equal(HttpStatusCode.OK, csrf.StatusCode);

        using var refresh = await client.PostAsync("/api/auth/refresh", null);
        using var resend = await client.PostAsJsonAsync("/api/auth/resend-confirmation", new { email = "user@example.test" });
        using var forgot = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email = "user@example.test" });
        using var reset = await client.PostAsJsonAsync("/api/auth/reset-password", new { email = "user@example.test", code = "x", newPassword = "SecurePass123" });
        Assert.All(new[] { refresh, resend, forgot, reset }, response => Assert.Equal((HttpStatusCode)429, response.StatusCode));

        using var logout = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.BadRequest, logout.StatusCode);
        using var live = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }
}
