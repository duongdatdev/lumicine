using LumiCine.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace LumiCine.IntegrationTests.Persistence;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class ShowtimeOverlapConstraintTests(PostgresDatabaseFixture fixture)
{
    [Fact]
    public async Task Same_room_allows_exact_cleanup_boundary_and_rejects_one_second_less()
    {
        await using var db = fixture.CreateDbContext();
        var (movie, _, room) = await DatabaseTestData.CreateBaseAsync(db);
        var start = DateTime.UtcNow.AddDays(2);
        var first = new Showtime(Guid.NewGuid(), movie.Id, room.Id, start, start.AddMinutes(90), 100, DateTime.UtcNow);
        var atBoundary = new Showtime(Guid.NewGuid(), movie.Id, room.Id, start.AddMinutes(105), start.AddMinutes(195), 100, DateTime.UtcNow);
        db.Showtimes.AddRange(first, atBoundary);
        await db.SaveChangesAsync();

        var oneSecondEarly = new Showtime(Guid.NewGuid(), movie.Id, room.Id, start.AddMinutes(105).AddSeconds(-1), start.AddMinutes(195).AddSeconds(-1), 100, DateTime.UtcNow);
        db.Showtimes.Add(oneSecondEarly);
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        Assert.Equal(PostgresErrorCodes.ExclusionViolation, Assert.IsType<PostgresException>(exception.InnerException).SqlState);
    }

    [Fact]
    public async Task Cancelled_showtime_does_not_block_an_overlapping_insert()
    {
        await using var db = fixture.CreateDbContext();
        var (movie, _, room) = await DatabaseTestData.CreateBaseAsync(db);
        var start = DateTime.UtcNow.AddDays(3);
        db.Showtimes.Add(new Showtime(Guid.NewGuid(), movie.Id, room.Id, start, start.AddHours(2), 100, DateTime.UtcNow, ShowtimeStatus.Cancelled));
        db.Showtimes.Add(new Showtime(Guid.NewGuid(), movie.Id, room.Id, start.AddMinutes(10), start.AddMinutes(110), 100, DateTime.UtcNow));

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Different_room_allows_the_same_time_range()
    {
        await using var db = fixture.CreateDbContext();
        var (movie, cinema, room) = await DatabaseTestData.CreateBaseAsync(db);
        var otherRoom = await DatabaseTestData.CreateRoomAsync(db, cinema.Id);
        var start = DateTime.UtcNow.AddDays(4);
        db.Showtimes.AddRange(
            new Showtime(Guid.NewGuid(), movie.Id, room.Id, start, start.AddHours(2), 100, DateTime.UtcNow),
            new Showtime(Guid.NewGuid(), movie.Id, otherRoom.Id, start, start.AddHours(2), 100, DateTime.UtcNow));

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Direct_sql_insert_is_rejected_by_exclusion_constraint()
    {
        await using var db = fixture.CreateDbContext();
        var (movie, _, room) = await DatabaseTestData.CreateBaseAsync(db);
        var start = DateTime.UtcNow.AddDays(6);
        db.Showtimes.Add(new Showtime(Guid.NewGuid(), movie.Id, room.Id, start, start.AddHours(2), 100, DateTime.UtcNow));
        await db.SaveChangesAsync();
        var overlappingStart = start.AddMinutes(100);
        var exception = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Showtimes" ("Id", "MovieId", "RoomId", "StartAtUtc", "EndAtUtc", "BasePrice", "Status", "CreatedAtUtc", "UpdatedAtUtc")
            VALUES ({Guid.NewGuid()}, {movie.Id}, {room.Id}, {overlappingStart}, {overlappingStart.AddHours(2)}, {100m}, {"Scheduled"}, {DateTime.UtcNow}, {null})
            """));

        Assert.Equal(PostgresErrorCodes.ExclusionViolation, exception.SqlState);
    }
}
