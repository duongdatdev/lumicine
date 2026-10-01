using LumiCine.Application.Common;
using LumiCine.Application.Scheduling;
using LumiCine.Domain.Scheduling;
using LumiCine.Infrastructure.Scheduling;
using LumiCine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LumiCine.IntegrationTests.Persistence;

[Collection(PostgresDatabaseCollection.Name)]
public sealed partial class ShowtimeServiceTests(PostgresDatabaseFixture fixture)
{
    

    [Fact]
    public async Task Concurrent_creates_for_same_room_have_exactly_one_success()
    {
        Guid movieId, roomId;
        await using (var seed = fixture.CreateDbContext())
        {
            (var movie, _, var room) = await DatabaseTestData.CreateBaseAsync(seed);
            await DatabaseTestData.CreateSeatAsync(seed, room.Id);
            movieId = movie.Id;
            roomId = room.Id;
        }
        var start = DateTimeOffset.UtcNow.AddDays(5).AddMinutes(30).ToString("O");
        async Task<bool> Create()
        {
            await using var db = fixture.CreateDbContext();
            try
            {
                await new ShowtimeService(db, TimeProvider.System).CreateAsync(new(movieId, roomId, start, 100), default);
                return true;
            }
            catch (ApiException ex) when (ex.StatusCode == 409) { return false; }
        }

        var results = await Task.WhenAll(Create(), Create());
        Assert.Single(results, x => x);
        Assert.Single(results, x => !x);
    }

    

    [Fact]
    public async Task Create_rejects_timestamp_without_explicit_offset_before_database_work()
    {
        await using var db = fixture.CreateDbContext();
        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            new ShowtimeService(db, TimeProvider.System).CreateAsync(
                new(Guid.NewGuid(), Guid.NewGuid(), "2030-01-01T20:00:00", 100), default));
        Assert.Equal(400, ex.StatusCode);
    }

    

    

    [Fact]
    public async Task Failed_seat_snapshot_save_rolls_back_showtime_insert()
    {
        Guid movieId, roomId;
        await using (var seed = fixture.CreateDbContext())
        {
            var (movie, _, room) = await DatabaseTestData.CreateBaseAsync(seed);
            await DatabaseTestData.CreateSeatAsync(seed, room.Id);
            movieId = movie.Id;
            roomId = room.Id;
        }
        var options = new DbContextOptionsBuilder<LumiCineDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .AddInterceptors(new RejectSeatSnapshotInterceptor())
            .Options;
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using (var db = new LumiCineDbContext(options))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => new ShowtimeService(db, TimeProvider.System)
                .CreateWithIdAsync(id, new(movieId, roomId, now.AddDays(4).ToString("O"), 90), default));
        }
        await using var verify = fixture.CreateDbContext();
        Assert.False(await verify.Showtimes.AnyAsync(x => x.Id == id));
        Assert.False(await verify.ShowtimeSeats.AnyAsync(x => x.ShowtimeId == id));
    }

    

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RejectSeatSnapshotInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<ShowtimeSeat>().Any(entry => entry.State == EntityState.Added) == true)
                return ValueTask.FromException<InterceptionResult<int>>(new InvalidOperationException("Injected snapshot failure."));
            return ValueTask.FromResult(result);
        }
    }
}

