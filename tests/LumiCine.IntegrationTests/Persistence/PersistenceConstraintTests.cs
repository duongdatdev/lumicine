using LumiCine.Domain.Scheduling;
using LumiCine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LumiCine.IntegrationTests.Persistence;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class PersistenceConstraintTests(PostgresDatabaseFixture fixture)
{
    [Fact]
    public async Task Room_code_is_unique_within_a_cinema()
    {
        await using var db = fixture.CreateDbContext();
        var (_, cinema, room) = await DatabaseTestData.CreateBaseAsync(db);
        FormattableString sql = $"""
            INSERT INTO "Rooms" ("Id", "CinemaId", "Code", "Name", "IsActive", "CreatedAtUtc")
            VALUES ({Guid.NewGuid()}, {cinema.Id}, {room.Code}, {"Duplicate"}, TRUE, {DateTime.UtcNow})
            """;

        await DatabaseAssertions.ThrowsSqlStateAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync(sql),
            PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task Seat_row_and_number_are_unique_within_a_room()
    {
        await using var db = fixture.CreateDbContext();
        var (_, _, room) = await DatabaseTestData.CreateBaseAsync(db);
        var seat = await DatabaseTestData.CreateSeatAsync(db, room.Id, "A", 1);
        FormattableString sql = $"""
            INSERT INTO "Seats" ("Id", "RoomId", "RowLabel", "Number", "SeatType", "IsActive", "CreatedAtUtc")
            VALUES ({Guid.NewGuid()}, {room.Id}, {seat.RowLabel}, {seat.Number}, {seat.SeatType}, TRUE, {DateTime.UtcNow})
            """;

        await DatabaseAssertions.ThrowsSqlStateAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync(sql),
            PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task Showtime_seat_is_unique_for_a_showtime()
    {
        await using var db = fixture.CreateDbContext();
        var (movie, _, room) = await DatabaseTestData.CreateBaseAsync(db);
        var seat = await DatabaseTestData.CreateSeatAsync(db, room.Id);
        var showtime = await DatabaseTestData.CreateShowtimeAsync(db, movie.Id, room.Id);
        await DatabaseTestData.CreateShowtimeSeatAsync(db, showtime, seat);
        FormattableString sql = $"""
            INSERT INTO "ShowtimeSeats" ("Id", "ShowtimeId", "RoomId", "SeatId", "Status", "Price", "CreatedAtUtc")
            VALUES ({Guid.NewGuid()}, {showtime.Id}, {room.Id}, {seat.Id}, {"Available"}, {100m}, {DateTime.UtcNow})
            """;

        await DatabaseAssertions.ThrowsSqlStateAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync(sql),
            PostgresErrorCodes.UniqueViolation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Showtime_seat_composite_foreign_keys_enforce_same_room(bool useSeatRoom)
    {
        await using var db = fixture.CreateDbContext();
        var (movie, cinema, room) = await DatabaseTestData.CreateBaseAsync(db);
        var otherRoom = await DatabaseTestData.CreateRoomAsync(db, cinema.Id);
        var otherRoomSeat = await DatabaseTestData.CreateSeatAsync(db, otherRoom.Id);
        var showtime = await DatabaseTestData.CreateShowtimeAsync(db, movie.Id, room.Id);
        var roomId = useSeatRoom ? otherRoom.Id : room.Id;
        FormattableString sql = $"""
            INSERT INTO "ShowtimeSeats" ("Id", "ShowtimeId", "RoomId", "SeatId", "Status", "Price", "CreatedAtUtc")
            VALUES ({Guid.NewGuid()}, {showtime.Id}, {roomId}, {otherRoomSeat.Id}, {"Available"}, {100m}, {DateTime.UtcNow})
            """;

        await DatabaseAssertions.ThrowsSqlStateAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync(sql),
            PostgresErrorCodes.ForeignKeyViolation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Booking_item_composite_foreign_keys_enforce_same_showtime(bool useSeatShowtime)
    {
        await using var db = fixture.CreateDbContext();
        var (movie, _, room) = await DatabaseTestData.CreateBaseAsync(db);
        var seat = await DatabaseTestData.CreateSeatAsync(db, room.Id);
        var firstShowtime = await DatabaseTestData.CreateShowtimeAsync(db, movie.Id, room.Id);
        var secondShowtime = await DatabaseTestData.CreateShowtimeAsync(db, movie.Id, room.Id, 180);
        var secondShowtimeSeat = await DatabaseTestData.CreateShowtimeSeatAsync(db, secondShowtime, seat);
        var booking = await DatabaseTestData.CreateBookingAsync(db, firstShowtime.Id);
        var itemShowtimeId = useSeatShowtime ? secondShowtime.Id : booking.ShowtimeId;
        FormattableString sql = $"""
            INSERT INTO "BookingItems" ("Id", "BookingId", "ShowtimeId", "ShowtimeSeatId", "UnitPrice", "CreatedAtUtc")
            VALUES ({Guid.NewGuid()}, {booking.Id}, {itemShowtimeId}, {secondShowtimeSeat.Id}, {100m}, {DateTime.UtcNow})
            """;

        await DatabaseAssertions.ThrowsSqlStateAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync(sql),
            PostgresErrorCodes.ForeignKeyViolation);
    }

    [Fact]
    public async Task Booking_code_is_unique()
    {
        await using var db = fixture.CreateDbContext();
        var (movie, _, room) = await DatabaseTestData.CreateBaseAsync(db);
        var showtime = await DatabaseTestData.CreateShowtimeAsync(db, movie.Id, room.Id);
        var booking = await DatabaseTestData.CreateBookingAsync(db, showtime.Id);
        FormattableString sql = $"""
            INSERT INTO "Bookings" ("Id", "Code", "ShowtimeId", "Status", "TotalAmount", "CreatedAtUtc")
            VALUES ({Guid.NewGuid()}, {booking.Code}, {booking.ShowtimeId}, {"Pending"}, {0m}, {DateTime.UtcNow})
            """;

        await DatabaseAssertions.ThrowsSqlStateAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync(sql),
            PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task Booking_items_are_unique_by_booking_and_showtime_seat()
    {
        await using var db = fixture.CreateDbContext();
        var (movie, _, room) = await DatabaseTestData.CreateBaseAsync(db);
        var seat = await DatabaseTestData.CreateSeatAsync(db, room.Id);
        var showtime = await DatabaseTestData.CreateShowtimeAsync(db, movie.Id, room.Id);
        var showtimeSeat = await DatabaseTestData.CreateShowtimeSeatAsync(db, showtime, seat);
        var booking = await DatabaseTestData.CreateBookingAsync(db, showtime.Id);
        await DatabaseTestData.CreateBookingItemAsync(db, booking, showtimeSeat);
        FormattableString sql = $"""
            INSERT INTO "BookingItems" ("Id", "BookingId", "ShowtimeId", "ShowtimeSeatId", "UnitPrice", "CreatedAtUtc")
            VALUES ({Guid.NewGuid()}, {booking.Id}, {booking.ShowtimeId}, {showtimeSeat.Id}, {100m}, {DateTime.UtcNow})
            """;

        await DatabaseAssertions.ThrowsSqlStateAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync(sql),
            PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task Ticket_code_and_booking_item_are_each_unique()
    {
        await using var db = fixture.CreateDbContext();
        var (movie, _, room) = await DatabaseTestData.CreateBaseAsync(db);
        var firstSeat = await DatabaseTestData.CreateSeatAsync(db, room.Id, "A", 1);
        var secondSeat = await DatabaseTestData.CreateSeatAsync(db, room.Id, "A", 2);
        var showtime = await DatabaseTestData.CreateShowtimeAsync(db, movie.Id, room.Id);
        var firstShowtimeSeat = await DatabaseTestData.CreateShowtimeSeatAsync(db, showtime, firstSeat);
        var secondShowtimeSeat = await DatabaseTestData.CreateShowtimeSeatAsync(db, showtime, secondSeat);
        var booking = await DatabaseTestData.CreateBookingAsync(db, showtime.Id);
        var firstItem = await DatabaseTestData.CreateBookingItemAsync(db, booking, firstShowtimeSeat);
        var secondItem = await DatabaseTestData.CreateBookingItemAsync(db, booking, secondShowtimeSeat);
        var firstTicket = await DatabaseTestData.CreateTicketAsync(db, firstItem.Id);
        FormattableString duplicateCode = $"""
            INSERT INTO "Tickets" ("Id", "BookingItemId", "Code", "Status", "IssuedAtUtc", "CreatedAtUtc")
            VALUES ({Guid.NewGuid()}, {secondItem.Id}, {firstTicket.Code}, {"Issued"}, {DateTime.UtcNow}, {DateTime.UtcNow})
            """;
        FormattableString duplicateItem = $"""
            INSERT INTO "Tickets" ("Id", "BookingItemId", "Code", "Status", "IssuedAtUtc", "CreatedAtUtc")
            VALUES ({Guid.NewGuid()}, {firstItem.Id}, {$"T-{Guid.NewGuid():N}"}, {"Issued"}, {DateTime.UtcNow}, {DateTime.UtcNow})
            """;

        await DatabaseAssertions.ThrowsSqlStateAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync(duplicateCode),
            PostgresErrorCodes.UniqueViolation);
        await DatabaseAssertions.ThrowsSqlStateAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync(duplicateItem),
            PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task Database_rejects_invalid_duration_seat_number_money_and_time_range()
    {
        await using var db = fixture.CreateDbContext();
        var (movie, _, room) = await DatabaseTestData.CreateBaseAsync(db);
        var seat = await DatabaseTestData.CreateSeatAsync(db, room.Id, "A", 1);
        var priceConstraintSeat = await DatabaseTestData.CreateSeatAsync(db, room.Id, "A", 2);
        var itemConstraintSeat = await DatabaseTestData.CreateSeatAsync(db, room.Id, "A", 3);
        var showtime = await DatabaseTestData.CreateShowtimeAsync(db, movie.Id, room.Id);
        var showtimeSeat = await DatabaseTestData.CreateShowtimeSeatAsync(db, showtime, seat);
        var itemConstraintShowtimeSeat = await DatabaseTestData.CreateShowtimeSeatAsync(db, showtime, itemConstraintSeat);
        var booking = await DatabaseTestData.CreateBookingAsync(db, showtime.Id);
        await DatabaseTestData.CreateBookingItemAsync(db, booking, showtimeSeat);
        var now = DateTime.UtcNow;

        await AssertCheckViolationAsync(db, $"""
            INSERT INTO "Movies" ("Id", "Title", "DurationMinutes", "CreatedAtUtc")
            VALUES ({Guid.NewGuid()}, {"Invalid duration"}, {0}, {now})
            """);
        await AssertCheckViolationAsync(db, $"""
            INSERT INTO "Seats" ("Id", "RoomId", "RowLabel", "Number", "SeatType", "IsActive", "CreatedAtUtc")
            VALUES ({Guid.NewGuid()}, {room.Id}, {"B"}, {0}, {"Standard"}, TRUE, {now})
            """);
        await AssertCheckViolationAsync(db, $"""
            INSERT INTO "Showtimes" ("Id", "MovieId", "RoomId", "StartAtUtc", "EndAtUtc", "BasePrice", "Status", "CreatedAtUtc")
            VALUES ({Guid.NewGuid()}, {movie.Id}, {room.Id}, {now}, {now}, {100m}, {"Scheduled"}, {now})
            """);
        await AssertCheckViolationAsync(db, $"""
            INSERT INTO "Showtimes" ("Id", "MovieId", "RoomId", "StartAtUtc", "EndAtUtc", "BasePrice", "Status", "CreatedAtUtc")
            VALUES ({Guid.NewGuid()}, {movie.Id}, {room.Id}, {now}, {now.AddHours(2)}, {-1m}, {"Scheduled"}, {now})
            """);
        await AssertCheckViolationAsync(db, $"""
            INSERT INTO "ShowtimeSeats" ("Id", "ShowtimeId", "RoomId", "SeatId", "Status", "Price", "CreatedAtUtc")
            VALUES ({Guid.NewGuid()}, {showtime.Id}, {room.Id}, {priceConstraintSeat.Id}, {"Available"}, {-1m}, {now})
            """);
        await AssertCheckViolationAsync(db, $"""
            INSERT INTO "Bookings" ("Id", "Code", "ShowtimeId", "Status", "TotalAmount", "CreatedAtUtc")
            VALUES ({Guid.NewGuid()}, {$"B-{Guid.NewGuid():N}"}, {showtime.Id}, {"Pending"}, {-1m}, {now})
            """);
        await AssertCheckViolationAsync(db, $"""
            INSERT INTO "BookingItems" ("Id", "BookingId", "ShowtimeId", "ShowtimeSeatId", "UnitPrice", "CreatedAtUtc")
            VALUES ({Guid.NewGuid()}, {booking.Id}, {booking.ShowtimeId}, {itemConstraintShowtimeSeat.Id}, {-1m}, {now})
            """);
    }

    [Fact]
    public async Task Booking_and_ticket_history_deletes_are_restricted()
    {
        await using var db = fixture.CreateDbContext();
        var (movie, _, room) = await DatabaseTestData.CreateBaseAsync(db);
        var seat = await DatabaseTestData.CreateSeatAsync(db, room.Id);
        var showtime = await DatabaseTestData.CreateShowtimeAsync(db, movie.Id, room.Id);
        var showtimeSeat = await DatabaseTestData.CreateShowtimeSeatAsync(db, showtime, seat);
        var booking = await DatabaseTestData.CreateBookingAsync(db, showtime.Id);
        var item = await DatabaseTestData.CreateBookingItemAsync(db, booking, showtimeSeat);
        await DatabaseTestData.CreateTicketAsync(db, item.Id);

        await DatabaseAssertions.ThrowsSqlStateAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"Bookings\" WHERE \"Id\" = {booking.Id}"),
            "23001");
        await DatabaseAssertions.ThrowsSqlStateAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"BookingItems\" WHERE \"Id\" = {item.Id}"),
            "23001");
    }

    private static Task AssertCheckViolationAsync(LumiCineDbContext db, FormattableString sql) =>
        DatabaseAssertions.ThrowsSqlStateAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync(sql),
            PostgresErrorCodes.CheckViolation);
}