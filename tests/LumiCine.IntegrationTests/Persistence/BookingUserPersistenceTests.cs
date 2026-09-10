using LumiCine.Domain.Bookings;
using LumiCine.Infrastructure.Identity;
using LumiCine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace LumiCine.IntegrationTests.Persistence;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class BookingUserPersistenceTests(PostgresDatabaseFixture fixture)
{
    [Fact]
    public async Task Existing_booking_history_remains_unowned_after_user_link_migration()
    {
        await using var db = fixture.CreateDbContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260915134856_SeedAccountRoles");

        var (movie, _, room) = await DatabaseTestData.CreateBaseAsync(db);
        var showtime = await DatabaseTestData.CreateShowtimeAsync(db, movie.Id, room.Id);
        var bookingId = Guid.NewGuid();
        var bookingCode = $"B-{Guid.NewGuid():N}";
        FormattableString legacyBooking = $"""
            INSERT INTO "Bookings" ("Id", "Code", "ShowtimeId", "Status", "TotalAmount", "CreatedAtUtc")
            VALUES ({bookingId}, {bookingCode}, {showtime.Id}, {"Pending"}, {100m}, {DateTime.UtcNow})
            """;
        await db.Database.ExecuteSqlInterpolatedAsync(legacyBooking);

        await migrator.MigrateAsync();

        Assert.Null((await db.Bookings.SingleAsync(booking => booking.Id == bookingId)).UserId);
    }

    [Fact]
    public async Task Booking_can_reference_an_existing_user()
    {
        await using var db = fixture.CreateDbContext();
        var user = await CreateUserAsync(db);
        var (movie, _, room) = await DatabaseTestData.CreateBaseAsync(db);
        var showtime = await DatabaseTestData.CreateShowtimeAsync(db, movie.Id, room.Id);
        var booking = new Booking(Guid.NewGuid(), $"B-{Guid.NewGuid():N}", showtime.Id, 100, DateTime.UtcNow, userId: user.Id);

        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        Assert.Equal(user.Id, (await db.Bookings.SingleAsync(item => item.Id == booking.Id)).UserId);
    }

    [Fact]
    public async Task Booking_rejects_a_missing_user()
    {
        await using var db = fixture.CreateDbContext();
        var (movie, _, room) = await DatabaseTestData.CreateBaseAsync(db);
        var showtime = await DatabaseTestData.CreateShowtimeAsync(db, movie.Id, room.Id);
        FormattableString invalidBooking = $"""
            INSERT INTO "Bookings" ("Id", "Code", "ShowtimeId", "Status", "TotalAmount", "CreatedAtUtc", "UserId")
            VALUES ({Guid.NewGuid()}, {$"B-{Guid.NewGuid():N}"}, {showtime.Id}, {"Pending"}, {100m}, {DateTime.UtcNow}, {Guid.NewGuid()})
            """;

        await DatabaseAssertions.ThrowsSqlStateAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync(invalidBooking),
            PostgresErrorCodes.ForeignKeyViolation);
    }

    [Fact]
    public async Task User_delete_is_restricted_while_booking_history_references_them()
    {
        await using var db = fixture.CreateDbContext();
        var user = await CreateUserAsync(db);
        var (movie, _, room) = await DatabaseTestData.CreateBaseAsync(db);
        var showtime = await DatabaseTestData.CreateShowtimeAsync(db, movie.Id, room.Id);
        db.Bookings.Add(new Booking(Guid.NewGuid(), $"B-{Guid.NewGuid():N}", showtime.Id, 100, DateTime.UtcNow, userId: user.Id));
        await db.SaveChangesAsync();
        FormattableString deleteUser = $"DELETE FROM \"Users\" WHERE \"Id\" = {user.Id}";

        await DatabaseAssertions.ThrowsSqlStateAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync(deleteUser),
            "23001");
    }

    private static async Task<ApplicationUser> CreateUserAsync(LumiCineDbContext db)
    {
        var email = $"{Guid.NewGuid():N}@example.com";
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            DisplayName = "Booking test user"
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }
}
