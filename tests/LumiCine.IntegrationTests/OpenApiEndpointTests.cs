namespace LumiCine.IntegrationTests;

public sealed class OpenApiEndpointTests
{
    [Fact]
    public async Task Development_exposes_openapi_document_and_swagger_ui()
    {
        using var factory = new ApiWebApplicationFactory("Development");
        using var client = factory.CreateClient();

        using var documentResponse = await client.GetAsync("/openapi/v1.json");
        using var uiResponse = await client.GetAsync("/swagger/index.html");

        Assert.Equal(System.Net.HttpStatusCode.OK, documentResponse.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, uiResponse.StatusCode);
    }

    [Fact]
    public async Task Production_hides_openapi_document_and_swagger_ui()
    {
        using var factory = new ApiWebApplicationFactory("Production");
        using var client = factory.CreateClient();

        using var documentResponse = await client.GetAsync("/openapi/v1.json");
        using var uiResponse = await client.GetAsync("/swagger/index.html");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, documentResponse.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, uiResponse.StatusCode);
    }
}
