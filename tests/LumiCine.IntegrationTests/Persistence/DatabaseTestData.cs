using LumiCine.Domain.Bookings;
using LumiCine.Domain.Catalog;
using LumiCine.Domain.Cinemas;
using LumiCine.Domain.Scheduling;
using LumiCine.Infrastructure.Persistence;

namespace LumiCine.IntegrationTests.Persistence;

internal static class DatabaseTestData
{
    public static async Task<(Movie Movie, Cinema Cinema, Room Room)> CreateBaseAsync(
        LumiCineDbContext context,
        bool movieIsActive = true,
        bool cinemaIsActive = true,
        bool roomIsActive = true)
    {
        var now = DateTime.UtcNow;
        var movie = new Movie(Guid.NewGuid(), $"Film-{Guid.NewGuid():N}", 90, now, isActive: movieIsActive);
        var cinema = new Cinema(Guid.NewGuid(), $"C-{Guid.NewGuid():N}"[..22], "Test Cinema", "Test Address", "Test City", now, cinemaIsActive);
        var room = new Room(Guid.NewGuid(), cinema.Id, $"R-{Guid.NewGuid():N}"[..22], "Test Room", now, roomIsActive);
        await context.AddRangeAsync(movie, cinema, room);
        await context.SaveChangesAsync();
        return (movie, cinema, room);
    }

    public static async Task<Room> CreateRoomAsync(LumiCineDbContext context, Guid cinemaId)
    {
        var room = new Room(Guid.NewGuid(), cinemaId, $"R-{Guid.NewGuid():N}"[..22], "Test Room", DateTime.UtcNow);
        await context.Rooms.AddAsync(room);
        await context.SaveChangesAsync();
        return room;
    }

    public static async Task<Seat> CreateSeatAsync(
        LumiCineDbContext context,
        Guid roomId,
        string rowLabel = "A",
        int number = 1,
        bool isActive = true)
    {
        var seat = new Seat(Guid.NewGuid(), roomId, rowLabel, number, "Standard", DateTime.UtcNow, isActive);
        await context.Seats.AddAsync(seat);
        await context.SaveChangesAsync();
        return seat;
    }

    public static async Task<Showtime> CreateShowtimeAsync(
        LumiCineDbContext context,
        Guid movieId,
        Guid roomId,
        int offsetMinutes = 0)
    {
        var start = DateTime.UtcNow.AddDays(1).AddMinutes(offsetMinutes);
        var showtime = new Showtime(Guid.NewGuid(), movieId, roomId, start, start.AddMinutes(100), 100, DateTime.UtcNow);
        await context.Showtimes.AddAsync(showtime);
        await context.SaveChangesAsync();
        return showtime;
    }

    public static async Task<ShowtimeSeat> CreateShowtimeSeatAsync(
        LumiCineDbContext context,
        Showtime showtime,
        Seat seat)
    {
        var showtimeSeat = new ShowtimeSeat(Guid.NewGuid(), showtime.Id, showtime.RoomId, seat.Id, 100, DateTime.UtcNow);
        await context.ShowtimeSeats.AddAsync(showtimeSeat);
        await context.SaveChangesAsync();
        return showtimeSeat;
    }

    public static async Task<Booking> CreateBookingAsync(
        LumiCineDbContext context,
        Guid showtimeId,
        BookingStatus status = BookingStatus.Pending)
    {
        var booking = new Booking(Guid.NewGuid(), $"B-{Guid.NewGuid():N}", showtimeId, 100, DateTime.UtcNow, status);
        await context.Bookings.AddAsync(booking);
        await context.SaveChangesAsync();
        return booking;
    }

    public static async Task<BookingItem> CreateBookingItemAsync(
        LumiCineDbContext context,
        Booking booking,
        ShowtimeSeat showtimeSeat)
    {
        var bookingItem = new BookingItem(Guid.NewGuid(), booking.Id, booking.ShowtimeId, showtimeSeat.Id, 100, DateTime.UtcNow);
        await context.BookingItems.AddAsync(bookingItem);
        await context.SaveChangesAsync();
        return bookingItem;
    }

    public static async Task<Ticket> CreateTicketAsync(LumiCineDbContext context, Guid bookingItemId)
    {
        var now = DateTime.UtcNow;
        var ticket = new Ticket(Guid.NewGuid(), bookingItemId, $"T-{Guid.NewGuid():N}", now, now);
        await context.Tickets.AddAsync(ticket);
        await context.SaveChangesAsync();
        return ticket;
    }
}