using LumiCine.Domain.Bookings;
using LumiCine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LumiCine.IntegrationTests.Persistence;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class DatabaseBehaviorTests(PostgresDatabaseFixture fixture)
{
    [Fact]
    public async Task Same_physical_seat_can_be_used_in_different_showtimes()
    {
        await using var db = fixture.CreateDbContext();
        var (movie, _, room) = await DatabaseTestData.CreateBaseAsync(db);
        var seat = await DatabaseTestData.CreateSeatAsync(db, room.Id);
        var firstShowtime = await DatabaseTestData.CreateShowtimeAsync(db, movie.Id, room.Id);
        var secondShowtime = await DatabaseTestData.CreateShowtimeAsync(db, movie.Id, room.Id, 180);

        await DatabaseTestData.CreateShowtimeSeatAsync(db, firstShowtime, seat);
        await DatabaseTestData.CreateShowtimeSeatAsync(db, secondShowtime, seat);

        Assert.Equal(2, await db.ShowtimeSeats.CountAsync(x => x.SeatId == seat.Id));
    }

    [Fact]
    public async Task Expired_booking_history_does_not_prevent_rebooking_a_seat()
    {
        await using var db = fixture.CreateDbContext();
        var (movie, _, room) = await DatabaseTestData.CreateBaseAsync(db);
        var seat = await DatabaseTestData.CreateSeatAsync(db, room.Id);
        var showtime = await DatabaseTestData.CreateShowtimeAsync(db, movie.Id, room.Id);
        var showtimeSeat = await DatabaseTestData.CreateShowtimeSeatAsync(db, showtime, seat);
        var expiredBooking = await DatabaseTestData.CreateBookingAsync(db, showtime.Id, BookingStatus.Expired);
        await DatabaseTestData.CreateBookingItemAsync(db, expiredBooking, showtimeSeat);
        var newBooking = await DatabaseTestData.CreateBookingAsync(db, showtime.Id);
        await DatabaseTestData.CreateBookingItemAsync(db, newBooking, showtimeSeat);

        Assert.Equal(2, await db.BookingItems.CountAsync(x => x.ShowtimeSeatId == showtimeSeat.Id));
    }

    [Fact]
    public async Task Explicit_false_active_values_survive_database_roundtrip()
    {
        await using var db = fixture.CreateDbContext();
        var (movie, cinema, room) = await DatabaseTestData.CreateBaseAsync(
            db,
            movieIsActive: false,
            cinemaIsActive: false,
            roomIsActive: false);
        var seat = await DatabaseTestData.CreateSeatAsync(db, room.Id, isActive: false);
        db.ChangeTracker.Clear();

        Assert.False((await db.Movies.AsNoTracking().SingleAsync(x => x.Id == movie.Id)).IsActive);
        Assert.False((await db.Cinemas.AsNoTracking().SingleAsync(x => x.Id == cinema.Id)).IsActive);
        Assert.False((await db.Rooms.AsNoTracking().SingleAsync(x => x.Id == room.Id)).IsActive);
        Assert.False((await db.Seats.AsNoTracking().SingleAsync(x => x.Id == seat.Id)).IsActive);
    }
}