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
    public async Task Two_independent_http_requests_for_same_room_return_one_created_and_one_conflict()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        Guid movieId, roomId;
        await using (var db = fixture.CreateDbContext())
        {
            var now = DateTime.UtcNow;
            var movie = new Movie(Guid.NewGuid(), "Concurrent Film", 90, now);
            var cinema = new Cinema(Guid.NewGuid(), $"C{Guid.NewGuid():N}"[..20], "Concurrent Cinema", "2 Main St", "Hanoi", now);
            var room = new Room(Guid.NewGuid(), cinema.Id, $"R{Guid.NewGuid():N}"[..20], "Room 2", now);
            db.AddRange(movie, cinema, room, new Seat(Guid.NewGuid(), room.Id, "A", 1, "VIP", now));
            await db.SaveChangesAsync();
            movieId = movie.Id;
            roomId = room.Id;
        }
        var one = await CreateAuthorizedClientAsync(factory, "Staff");
        var two = await CreateAuthorizedClientAsync(factory, "Staff");
        var start = DateTimeOffset.UtcNow.AddDays(3).ToString("O");
        object body = new { movieId, roomId, startAt = start, basePrice = 100m };
        var statuses = await Task.WhenAll(
            one.PostAsJsonAsync("/api/admin/showtimes", body),
            two.PostAsJsonAsync("/api/admin/showtimes", body));
        Assert.True(statuses.Count(x => x.StatusCode == HttpStatusCode.Created) == 1, string.Join("\n", await Task.WhenAll(statuses.Select(x => x.Content.ReadAsStringAsync()))));
        Assert.Equal(1, statuses.Count(x => x.StatusCode == HttpStatusCode.Conflict));
        foreach (var response in statuses) response.Dispose();
        one.Dispose();
        two.Dispose();
    }
}
