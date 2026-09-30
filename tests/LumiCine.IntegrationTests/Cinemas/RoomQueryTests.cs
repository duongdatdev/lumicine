using System.Net;
using System.Net.Http.Headers;
using LumiCine.Infrastructure.Cinemas;
using LumiCine.IntegrationTests.Persistence;

namespace LumiCine.IntegrationTests.Cinemas;

public sealed partial class CinemaServiceTests
{
    [Fact]
    public async Task Missing_cinema_room_collection_returns_null_for_http_404()
    {
        await using var db = Fixture.CreateDbContext();
        var service = new CinemaService(db, TimeProvider.System);
        Assert.Null(await service.GetAdminRoomsAsync(Guid.NewGuid(), new(), default));

        using var factory = new ApiWebApplicationFactory(connectionString: Fixture.ConnectionString);
        using var client = factory.CreateClient();
        var admin = await CreateAdminAsync(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.Token);
        using var response = await client.GetAsync($"/api/admin/cinemas/{Guid.NewGuid()}/rooms");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
