using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LumiCine.Application.Accounts;
using LumiCine.Domain.Catalog;
using LumiCine.Domain.Cinemas;
using LumiCine.Infrastructure.Authentication;
using LumiCine.Infrastructure.Identity;
using LumiCine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LumiCine.IntegrationTests.Persistence;

[Collection(PostgresDatabaseCollection.Name)]
public sealed partial class ShowtimesEndpointTests(PostgresDatabaseFixture fixture)
{
    [Fact]
    public async Task Public_browsing_and_staff_admin_management_roles_work()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        using var guest = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await guest.GetAsync("/api/showtimes")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync("/api/admin/showtimes")).StatusCode);

        var customer = await CreateAuthorizedClientAsync(factory, "Customer");
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/admin/showtimes")).StatusCode);
        customer.Dispose();

        Guid movieId, roomId;
        await using (var db = fixture.CreateDbContext())
        {
            var now = DateTime.UtcNow;
            var movie = new Movie(Guid.NewGuid(), "Endpoint Film", 90, now);
            var cinema = new Cinema(Guid.NewGuid(), $"C{Guid.NewGuid():N}"[..20], "Endpoint Cinema", "1 Main St", "Hanoi", now);
            var room = new Room(Guid.NewGuid(), cinema.Id, $"R{Guid.NewGuid():N}"[..20], "Room 1", now);
            var seat = new Seat(Guid.NewGuid(), room.Id, "A", 1, "Standard", now);
            db.AddRange(movie, cinema, room, seat);
            await db.SaveChangesAsync();
            movieId = movie.Id;
            roomId = room.Id;
        }

        var staff = await CreateAuthorizedClientAsync(factory, "Staff");
        var start = DateTimeOffset.UtcNow.AddDays(2);
        using var response = await staff.PostAsJsonAsync("/api/admin/showtimes", new
        {
            movieId, roomId, startAt = start.ToString("O"), basePrice = 120m
        });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        using var result = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var showtimeId = result.RootElement.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await guest.GetAsync($"/api/showtimes/{showtimeId}/seats")).StatusCode);
        staff.Dispose();

        var admin = await CreateAuthorizedClientAsync(factory, "Admin");
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/admin/showtimes/{showtimeId}")).StatusCode);
        admin.Dispose();
    }

    

    

    private async Task<HttpClient> CreateAuthorizedClientAsync(ApiWebApplicationFactory factory, string role)
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        await using (var db = fixture.CreateDbContext())
        {
            var email = $"{Guid.NewGuid():N}@example.com";
            var user = new ApplicationUser
            {
                Id = userId, Email = email, NormalizedEmail = email.ToUpperInvariant(),
                EmailConfirmed = true, IsActive = true, DisplayName = "Schedule test"
            };
            db.Users.Add(user);
            var dbRole = await db.Roles.SingleAsync(x => x.Name == role);
            db.UserRoles.Add(new() { UserId = userId, RoleId = dbRole.Id });
            db.AuthSessions.Add(new AuthSession { Id = sessionId, UserId = userId, CreatedAtUtc = DateTime.UtcNow, ExpiresAtUtc = DateTime.UtcNow.AddHours(1) });
            await db.SaveChangesAsync();
        }
        var client = factory.CreateClient();
        var token = factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(userId, sessionId, [role]).Value;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

}

