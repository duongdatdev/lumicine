using System.Net;
using System.Text.Json;

namespace LumiCine.IntegrationTests.Authentication;

public sealed class OpenApiAuthTests
{
    [Fact]
    public async Task Development_document_describes_bearer_csrf_and_public_operations()
    {
        using var factory = new ApiWebApplicationFactory(includeTestControllers: true);
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = document.RootElement;
        var bearer = root.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal("http", bearer.GetProperty("type").GetString());
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString());
        Assert.Equal("JWT", bearer.GetProperty("bearerFormat").GetString());

        var operations = root.GetProperty("paths");
        var protectedOperation = operations.GetProperty("/_test/auth/authenticated").GetProperty("get");
        Assert.True(protectedOperation.GetProperty("security").EnumerateArray().Any());
        var loginOperation = operations.GetProperty("/api/auth/login").GetProperty("post");
        Assert.False(loginOperation.TryGetProperty("security", out _));
        Assert.Contains(loginOperation.GetProperty("parameters").EnumerateArray(), parameter =>
            parameter.GetProperty("name").GetString() == "X-CSRF-TOKEN" && parameter.GetProperty("required").GetBoolean());
        Assert.False(operations.GetProperty("/health/live").GetProperty("get").TryGetProperty("security", out _));

        var csrfProtectedOperation = operations.GetProperty("/_test/auth/csrf-probe").GetProperty("post");
        Assert.Contains(csrfProtectedOperation.GetProperty("parameters").EnumerateArray(), parameter =>
            parameter.GetProperty("name").GetString() == "X-CSRF-TOKEN" && parameter.GetProperty("required").GetBoolean());
        Assert.False(operations.GetProperty("/api/auth/csrf").GetProperty("get").TryGetProperty("parameters", out _));
    }
}
