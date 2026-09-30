using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LumiCine.Application.Cinemas;
using LumiCine.Application.Common;
using LumiCine.Infrastructure.Cinemas;
using Microsoft.Extensions.DependencyInjection;

namespace LumiCine.IntegrationTests.Cinemas;

public sealed partial class CinemaServiceTests
{
    [Fact]
    public async Task Duplicate_cinema_code_returns_conflict()
    {
        await using var db = Fixture.CreateDbContext();
        var service = new CinemaService(db, TimeProvider.System);
        await service.CreateCinemaAsync(new(" dup ", "One", "Address", "City"), default);

        var error = await Assert.ThrowsAsync<ApiException>(() => service.CreateCinemaAsync(new("DUP", "Two", "Address", "City"), default));
        Assert.Equal(409, error.StatusCode);
    }

    [Fact]
    public async Task Duplicate_cinema_code_returns_http_409_and_missing_status_value_is_400()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: Fixture.ConnectionString);
        using var client = factory.CreateClient();
        var admin = await CreateAdminAsync(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.Token);

        using var invalidStatus = await client.PutAsJsonAsync($"/api/admin/cinemas/{admin.CinemaId}/status", new { });
        Assert.Equal(HttpStatusCode.BadRequest, invalidStatus.StatusCode);
        var data = new CinemaRequest("HTTP-DUP", "HTTP Cinema", "Address", "City");
        using var created = await client.PostAsJsonAsync("/api/admin/cinemas", data);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var conflict = await client.PostAsJsonAsync("/api/admin/cinemas", data with { Name = "Duplicate" });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    }
}
