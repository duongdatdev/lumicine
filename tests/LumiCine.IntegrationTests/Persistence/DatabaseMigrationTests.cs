using LumiCine.Infrastructure.Persistence;
using LumiCine.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LumiCine.IntegrationTests.Persistence;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class DatabaseMigrationTests(PostgresDatabaseFixture fixture)
{
    [Fact]
    public async Task Initial_migration_can_move_down_and_up()
    {
        await using var db = fixture.CreateDbContext();
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync("0");
        Assert.Empty(await db.Database.GetAppliedMigrationsAsync());

        await migrator.MigrateAsync();

        Assert.Contains("20260906160211_InitialCreate", await db.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Role_seed_migration_can_move_down_and_up()
    {
        await using var db = fixture.CreateDbContext();
        var migrator = db.GetService<IMigrator>();

        Assert.Equal(3, await db.Roles.CountAsync());

        await migrator.MigrateAsync("20260915130621_AddIdentity");
        Assert.Empty(await db.Roles.ToArrayAsync());

        await migrator.MigrateAsync();
        Assert.Equal(
            new[] { "Admin", "Customer", "Staff" },
            await db.Roles.OrderBy(role => role.Name).Select(role => role.Name!).ToArrayAsync());
    }

    [Fact]
    public async Task Reservation_migration_preserves_legacy_booking_and_ownerless_hold()
    {
        await using var db = fixture.CreateDbContext();
        var (movie, _, room) = await DatabaseTestData.CreateBaseAsync(db);
        var showtime = await DatabaseTestData.CreateShowtimeAsync(db, movie.Id, room.Id);
        var seat = await DatabaseTestData.CreateSeatAsync(db, room.Id);
        var snapshot = await DatabaseTestData.CreateShowtimeSeatAsync(db, showtime, seat);
        var booking = await DatabaseTestData.CreateBookingAsync(db, showtime.Id);
        await DatabaseTestData.CreateBookingItemAsync(db, booking, snapshot);
        await db.ShowtimeSeats.Where(x => x.Id == snapshot.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.Status, ShowtimeSeatStatus.Held));

        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261020120000_PreventOverlappingShowtimes");
        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();

        var legacy = await db.Bookings.SingleAsync(x => x.Id == booking.Id);
        Assert.Null(legacy.RequestId);
        Assert.Null(legacy.UserId);
        Assert.Null(legacy.HoldExpiresAtUtc);
        var hold = await db.ShowtimeSeats.SingleAsync(x => x.Id == snapshot.Id);
        Assert.Equal(ShowtimeSeatStatus.Held, hold.Status);
        Assert.Null(hold.CurrentBookingId);
        Assert.True(await db.BookingItems.AnyAsync(x => x.BookingId == booking.Id));
    }
}
