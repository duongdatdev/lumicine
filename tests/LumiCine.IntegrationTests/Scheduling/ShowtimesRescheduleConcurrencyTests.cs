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
namespace LumiCine.IntegrationTests.Persistence;

public sealed partial class ShowtimesEndpointTests
{
[Fact]
    public async Task Two_independent_http_reschedules_into_the_same_room_slot_return_one_success_and_one_conflict()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        Guid movieId, roomId;
        await using (var db = fixture.CreateDbContext())
        {
            var now = DateTime.UtcNow;
            var movie = new Movie(Guid.NewGuid(), "Concurrent Reschedule", 90, now);
            var cinema = new Cinema(Guid.NewGuid(), $"C{Guid.NewGuid():N}"[..20], "Reschedule Cinema", "3 Main St", "Hanoi", now);
            var room = new Room(Guid.NewGuid(), cinema.Id, $"R{Guid.NewGuid():N}"[..20], "Room 3", now);
            db.AddRange(movie, cinema, room, new Seat(Guid.NewGuid(), room.Id, "A", 1, "Standard", now));
            await db.SaveChangesAsync();
            movieId = movie.Id;
            roomId = room.Id;
        }
        var creator = await CreateAuthorizedClientAsync(factory, "Staff");
        async Task<Guid> CreateShowtime(DateTimeOffset start)
        {
            using var response = await creator.PostAsJsonAsync("/api/admin/showtimes", new
            { movieId, roomId, startAt = start.ToString("O"), basePrice = 100m });
            Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
            using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            return body.RootElement.GetProperty("id").GetGuid();
        }

        var first = await CreateShowtime(DateTimeOffset.UtcNow.AddDays(8));
        var second = await CreateShowtime(DateTimeOffset.UtcNow.AddDays(12));
        creator.Dispose();
        var one = await CreateAuthorizedClientAsync(factory, "Staff");
        var two = await CreateAuthorizedClientAsync(factory, "Staff");
        var target = DateTimeOffset.UtcNow.AddDays(16).ToString("O");
        var responses = await Task.WhenAll(
            one.PutAsJsonAsync($"/api/admin/showtimes/{first}", new { startAt = target, basePrice = 110m }),
            two.PutAsJsonAsync($"/api/admin/showtimes/{second}", new { startAt = target, basePrice = 110m }));

        Assert.Equal(1, responses.Count(x => x.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, responses.Count(x => x.StatusCode == HttpStatusCode.Conflict));
        foreach (var response in responses) response.Dispose();
        one.Dispose();
        two.Dispose();
    }
}
