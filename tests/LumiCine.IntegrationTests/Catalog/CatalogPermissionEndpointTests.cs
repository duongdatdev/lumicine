using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LumiCine.Application.Accounts;
using LumiCine.Infrastructure.Authentication;
using LumiCine.Infrastructure.Identity;
using LumiCine.IntegrationTests.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace LumiCine.IntegrationTests.Catalog;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class CatalogPermissionEndpointTests(PostgresDatabaseFixture database)
{
    [Fact]
    public async Task Public_reads_work_and_management_reads_writes_follow_role_matrix()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: database.ConnectionString);
        using var client = factory.CreateClient();
        var customer = await CreateUserAsync(factory, RoleNames.Customer);
        var staff = await CreateUserAsync(factory, RoleNames.Staff);
        var admin = await CreateUserAsync(factory, RoleNames.Admin);

        using (var movies = await client.GetAsync("/api/movies")) Assert.Equal(HttpStatusCode.OK, movies.StatusCode);
        using (var genres = await client.GetAsync("/api/genres")) Assert.Equal(HttpStatusCode.OK, genres.StatusCode);
        using (var adminRead = await client.GetAsync("/api/admin/movies")) Assert.Equal(HttpStatusCode.Unauthorized, adminRead.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", customer);
        using (var response = await client.GetAsync("/api/admin/movies")) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staff);
        using (var response = await client.GetAsync("/api/admin/movies")) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var response = await client.PostAsJsonAsync("/api/admin/genres", new { Name = "Forbidden", Slug = "forbidden" }))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var movieForm = new MultipartFormDataContent();
        using (var response = await client.PostAsync($"/api/admin/movies/{Guid.NewGuid():D}/poster", movieForm))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin);
        using var genreResponse = await client.PostAsJsonAsync("/api/admin/genres", new { Name = $"Allowed-{Guid.NewGuid():N}", Slug = $"allowed-{Guid.NewGuid():N}" });
        Assert.Equal(HttpStatusCode.Created, genreResponse.StatusCode);
        using var movieResponse = await client.PostAsJsonAsync("/api/admin/movies", new
        {
            Title = $"Catalog-{Guid.NewGuid():N}", DurationMinutes = 90, GenreIds = Array.Empty<Guid>()
        });
        Assert.Equal(HttpStatusCode.Created, movieResponse.StatusCode);
        using var body = await JsonDocument.ParseAsync(await movieResponse.Content.ReadAsStreamAsync());
        Assert.True(body.RootElement.GetProperty("isActive").GetBoolean());
        var movieId = body.RootElement.GetProperty("id").GetGuid();

        using (var missingStatus = await client.PutAsJsonAsync($"/api/admin/movies/{movieId:D}/status", new { }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, missingStatus.StatusCode);
            using var problem = await JsonDocument.ParseAsync(await missingStatus.Content.ReadAsStreamAsync());
            Assert.True(problem.RootElement.TryGetProperty("traceId", out _));
        }
        using (var stillActive = await client.GetAsync($"/api/admin/movies/{movieId:D}"))
        {
            using var activeBody = await JsonDocument.ParseAsync(await stillActive.Content.ReadAsStreamAsync());
            Assert.True(activeBody.RootElement.GetProperty("isActive").GetBoolean());
        }

        using var tooLargeForm = new MultipartFormDataContent();
        using var tooLargeFile = new ByteArrayContent(new byte[6 * 1024 * 1024 + 1]);
        tooLargeFile.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        tooLargeForm.Add(tooLargeFile, "File", "large.jpg");
        using var oversizedResponse = await client.PostAsync($"/api/admin/movies/{movieId:D}/poster", tooLargeForm);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversizedResponse.StatusCode);
        Assert.Equal("application/problem+json", oversizedResponse.Content.Headers.ContentType?.MediaType);
        using var oversizedBody = await JsonDocument.ParseAsync(await oversizedResponse.Content.ReadAsStreamAsync());
        Assert.True(oversizedBody.RootElement.TryGetProperty("traceId", out _));

        using var chunkedForm = new MultipartFormDataContent();
        using var chunkedFile = new ByteArrayContent(new byte[6 * 1024 * 1024 + 1]);
        chunkedFile.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        chunkedForm.Add(chunkedFile, "File", "large-chunked.jpg");
        using var chunkedContent = new UnknownLengthHttpContent(chunkedForm);
        using var chunkedResponse = await client.PostAsync($"/api/admin/movies/{movieId:D}/poster", chunkedContent);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, chunkedResponse.StatusCode);
        using var chunkedBody = await JsonDocument.ParseAsync(await chunkedResponse.Content.ReadAsStreamAsync());
        Assert.True(chunkedBody.RootElement.TryGetProperty("traceId", out _));

        using var multipleFilesForm = new MultipartFormDataContent();
        using var firstFile = new ByteArrayContent([1, 2, 3]);
        using var secondFile = new ByteArrayContent([1, 2, 3]);
        firstFile.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        secondFile.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        multipleFilesForm.Add(firstFile, "File", "first.jpg");
        multipleFilesForm.Add(secondFile, "File", "second.jpg");
        using var multipleResponse = await client.PostAsync($"/api/admin/movies/{movieId:D}/poster", multipleFilesForm);
        Assert.Equal(HttpStatusCode.BadRequest, multipleResponse.StatusCode);
    }

    private static async Task<string> CreateUserAsync(ApiWebApplicationFactory factory, string role)
    {
        using var scope = factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"catalog-{Guid.NewGuid():N}@example.test";
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = $"Catalog {role}",
            CreatedAtUtc = DateTime.UtcNow
        };
        var create = await manager.CreateAsync(user);
        Assert.True(create.Succeeded, string.Join("; ", create.Errors.Select(error => error.Description)));
        var addRole = await manager.AddToRoleAsync(user, role);
        Assert.True(addRole.Succeeded, string.Join("; ", addRole.Errors.Select(error => error.Description)));
        var db = scope.ServiceProvider.GetRequiredService<LumiCine.Infrastructure.Persistence.LumiCineDbContext>();
        var sessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        db.AuthSessions.Add(new AuthSession { Id = sessionId, UserId = user.Id, CreatedAtUtc = now, ExpiresAtUtc = now.AddDays(7) });
        await db.SaveChangesAsync();
        return factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(user.Id, sessionId, [role]).Value;
    }

    private sealed class UnknownLengthHttpContent(HttpContent inner) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => inner.CopyToAsync(stream);
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
