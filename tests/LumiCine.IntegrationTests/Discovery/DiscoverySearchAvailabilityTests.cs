using LumiCine.Application.Discovery;
using LumiCine.Domain.Bookings;
using LumiCine.Domain.Catalog;
using LumiCine.Domain.Cinemas;
using LumiCine.Domain.Scheduling;
using LumiCine.Infrastructure.Discovery;
using LumiCine.Infrastructure.Persistence;
using LumiCine.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LumiCine.IntegrationTests.Discovery;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class DiscoverySearchAvailabilityTests(PostgresDatabaseFixture fixture)
{
    [Fact]
    public async Task Sold_and_active_holds_are_excluded_while_expired_holds_count_as_available()
    {
        await using var db = fixture.CreateDbContext();
        var (_, room) = await SeedCinemaAsync(db);
        var movie = await SeedMovieAsync(db, "Siêu lũ qa1", "Phiêu lưu");
        var showtime = await SeedShowtimeAsync(db, movie.Id, room.Id);
        var soldBooking = await SeedBookingAsync(db, showtime.Id, BookingStatus.Confirmed);
        var activeHoldBooking = await SeedBookingAsync(db, showtime.Id, BookingStatus.Pending, DateTime.UtcNow.AddMinutes(30));
        var expiredBooking = await SeedBookingAsync(db, showtime.Id, BookingStatus.Expired);
        await SeedShowtimeSeatAsync(db, showtime, "A", 1, 100_000m, ShowtimeSeatStatus.Available);
        await SeedShowtimeSeatAsync(db, showtime, "B", 1, 90_000m, ShowtimeSeatStatus.Sold, soldBooking);
        await SeedShowtimeSeatAsync(db, showtime, "C", 1, 80_000m, ShowtimeSeatStatus.Held, activeHoldBooking);
        await SeedShowtimeSeatAsync(db, showtime, "D", 1, 60_000m, ShowtimeSeatStatus.Held, expiredBooking);
        var service = CreateService();

        var output = await service.SearchAsync(CreateInput(new DiscoveryFilters { MovieNameText = "qa1" }), default);

        var result = Assert.Single(output.Movies);
        Assert.Equal(movie.Id, result.MovieId);
        var offered = Assert.Single(result.Showtimes);
        Assert.Equal(showtime.Id, offered.ShowtimeId);
        Assert.Equal(2, offered.AvailableSeats); // the available seat plus the expired hold
        Assert.Equal(60_000m, offered.Price);    // cheapest available seat
    }

    [Fact]
    public async Task Max_price_excludes_showtimes_without_a_cheaper_available_seat()
    {
        await using var db = fixture.CreateDbContext();
        var (_, room) = await SeedCinemaAsync(db);
        var movie = await SeedMovieAsync(db, "Mưa qa2", "Tình cảm");
        var expensive = await SeedShowtimeAsync(db, movie.Id, room.Id, DateTime.UtcNow.AddDays(1));
        await SeedShowtimeSeatAsync(db, expensive, "A", 1, 150_000m);
        await SeedShowtimeSeatAsync(db, expensive, "A", 2, 180_000m);
        var affordable = await SeedShowtimeAsync(db, movie.Id, room.Id, DateTime.UtcNow.AddDays(1).AddMinutes(150));
        await SeedShowtimeSeatAsync(db, affordable, "A", 3, 100_000m);
        await SeedShowtimeSeatAsync(db, affordable, "A", 4, 120_000m);
        var service = CreateService();
        var filters = new DiscoveryFilters { MovieNameText = "qa2", MaxPrice = 120_000m };

        var output = await service.SearchAsync(CreateInput(filters), default);

        var result = Assert.Single(output.Movies);
        var offered = Assert.Single(result.Showtimes);
        Assert.Equal(affordable.Id, offered.ShowtimeId);
        Assert.Equal(100_000m, offered.Price);
        Assert.Equal(2, offered.AvailableSeats);
    }

    [Fact]
    public async Task Min_seats_excludes_showtimes_with_too_few_effective_seats()
    {
        await using var db = fixture.CreateDbContext();
        var (_, room) = await SeedCinemaAsync(db);
        var movie = await SeedMovieAsync(db, "Gió qa3", "Phiêu lưu");
        var scarce = await SeedShowtimeAsync(db, movie.Id, room.Id);
        var expiredBooking = await SeedBookingAsync(db, scarce.Id, BookingStatus.Expired);
        var activeHoldBooking = await SeedBookingAsync(db, scarce.Id, BookingStatus.Pending, DateTime.UtcNow.AddMinutes(30));
        var soldBooking = await SeedBookingAsync(db, scarce.Id, BookingStatus.Confirmed);
        await SeedShowtimeSeatAsync(db, scarce, "A", 1, 100_000m); // effective
        await SeedShowtimeSeatAsync(db, scarce, "A", 2, 100_000m, ShowtimeSeatStatus.Held, expiredBooking); // effective
        await SeedShowtimeSeatAsync(db, scarce, "A", 3, 100_000m, ShowtimeSeatStatus.Sold, soldBooking);
        await SeedShowtimeSeatAsync(db, scarce, "A", 4, 100_000m, ShowtimeSeatStatus.Held, activeHoldBooking);
        var roomy = await SeedShowtimeAsync(db, movie.Id, room.Id, DateTime.UtcNow.AddDays(1).AddMinutes(150));
        await SeedShowtimeSeatAsync(db, roomy, "A", 5, 100_000m);
        await SeedShowtimeSeatAsync(db, roomy, "A", 6, 100_000m);
        await SeedShowtimeSeatAsync(db, roomy, "A", 7, 100_000m);
        var service = CreateService();
        var filters = new DiscoveryFilters { MovieNameText = "qa3", MinSeats = 3 };

        var output = await service.SearchAsync(CreateInput(filters), default);

        var result = Assert.Single(output.Movies);
        var offered = Assert.Single(result.Showtimes);
        Assert.Equal(roomy.Id, offered.ShowtimeId);
        Assert.Equal(3, offered.AvailableSeats);
    }

    [Fact]
    public async Task Showtimes_outside_the_date_window_are_not_offered()
    {
        await using var db = fixture.CreateDbContext();
        var (_, room) = await SeedCinemaAsync(db);
        var movie = await SeedMovieAsync(db, "Nắng qa4", "Tình cảm");
        var soon = await SeedShowtimeAsync(db, movie.Id, room.Id, DateTime.UtcNow.AddDays(1));
        await SeedShowtimeAsync(db, movie.Id, room.Id, DateTime.UtcNow.AddDays(20));
        var service = CreateService();

        var output = await service.SearchAsync(CreateInput(new DiscoveryFilters { MovieNameText = "qa4" }), default);

        var result = Assert.Single(output.Movies);
        var offered = Assert.Single(result.Showtimes);
        Assert.Equal(soon.Id, offered.ShowtimeId);
    }

    [Fact]
    public async Task Wrapped_time_window_keeps_late_evening_and_drops_daytime_showtimes()
    {
        await using var db = fixture.CreateDbContext();
        var (_, room) = await SeedCinemaAsync(db);
        var movie = await SeedMovieAsync(db, "Sao qa5", "Kinh dị");
        var localTomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7).AddDays(1));
        var lateEvening = await SeedShowtimeAsync(db, movie.Id, room.Id, DiscoveryTime.ToUtc(localTomorrow, 23 * 60 + 30));
        await SeedShowtimeAsync(db, movie.Id, room.Id, DiscoveryTime.ToUtc(localTomorrow, 10 * 60));
        var service = CreateService();
        var filters = new DiscoveryFilters
        {
            MovieNameText = "qa5",
            TimeWindowStartMinutes = 22 * 60, // 22:00 -> 02:00 wraps past midnight
            TimeWindowEndMinutes = 2 * 60
        };

        var output = await service.SearchAsync(CreateInput(filters), default);

        var result = Assert.Single(output.Movies);
        var offered = Assert.Single(result.Showtimes);
        Assert.Equal(lateEvening.Id, offered.ShowtimeId);
        Assert.Equal(23 * 60 + 30, DiscoveryTime.MinutesOfDayInZone(offered.StartAtUtc));
    }

    [Fact]
    public async Task City_and_cinema_filters_narrow_the_offered_showtimes()
    {
        await using var db = fixture.CreateDbContext();
        // ASCII city names: SQL lower() only folds ASCII under this database's C locale.
        var (hanoi, hanoiRoom) = await SeedCinemaAsync(db, "Ha Noi");
        var (danang, danangRoom) = await SeedCinemaAsync(db, "Da Nang");
        var movie = await SeedMovieAsync(db, "Mây qa6", "Tình cảm");
        var hanoiShow = await SeedShowtimeAsync(db, movie.Id, hanoiRoom.Id);
        var danangShow = await SeedShowtimeAsync(db, movie.Id, danangRoom.Id, DateTime.UtcNow.AddDays(1).AddMinutes(150));
        var service = CreateService();

        var byCity = await service.SearchAsync(CreateInput(new DiscoveryFilters { MovieNameText = "qa6", City = "da nang" }), default);

        var cityResult = Assert.Single(byCity.Movies);
        Assert.Equal(danangShow.Id, Assert.Single(cityResult.Showtimes).ShowtimeId);

        var byCinema = await service.SearchAsync(CreateInput(new DiscoveryFilters { MovieNameText = "qa6", CinemaId = hanoi.Id }), default);

        var cinemaResult = Assert.Single(byCinema.Movies);
        var offered = Assert.Single(cinemaResult.Showtimes);
        Assert.Equal(hanoiShow.Id, offered.ShowtimeId);
        Assert.Equal(hanoi.Id, offered.CinemaId);
    }

    [Fact]
    public async Task Schedule_mode_orders_movies_by_their_earliest_showtime()
    {
        await using var db = fixture.CreateDbContext();
        var (_, room) = await SeedCinemaAsync(db);
        // The earlier showtime belongs to the larger id so Id order cannot explain the ranking.
        var earlyMovie = await SeedMovieAsync(db, Guid.Parse("f0000000-0000-0000-0000-000000000011"), "Băng qa7", "Hoạt hình");
        var lateMovie = await SeedMovieAsync(db, Guid.Parse("10000000-0000-0000-0000-000000000012"), "Tuyết qa7", "Hoạt hình");
        var day = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(15)); // only this test seeds that day
        await SeedShowtimeAsync(db, earlyMovie.Id, room.Id, DiscoveryTime.ToUtc(day, 8 * 60));
        await SeedShowtimeAsync(db, lateMovie.Id, room.Id, DiscoveryTime.ToUtc(day, 10 * 60));
        var service = CreateService();
        var filters = DiscoveryFilters.Empty() with { DateFrom = day, DateTo = day };

        var output = await service.SearchAsync(CreateInput(filters), default);

        Assert.Equal(DiscoveryModes.Schedule, output.Mode);
        Assert.All(output.Movies, movie => Assert.Equal(0, movie.Score));
        Assert.Equal([earlyMovie.Id, lateMovie.Id], output.Movies.Select(m => m.MovieId).ToList());
        var firstShowtime = Assert.Single(output.Movies[0].Showtimes);
        Assert.Equal(8 * 60, DiscoveryTime.MinutesOfDayInZone(firstShowtime.StartAtUtc));
    }

    [Fact]
    public async Task Movies_without_qualifying_showtimes_are_dropped_and_lower_ranks_move_up()
    {
        await using var db = fixture.CreateDbContext();
        var (_, room) = await SeedCinemaAsync(db);
        var soldOut = await SeedMovieAsync(db, Guid.Parse("10000000-0000-0000-0000-000000000021"), "Bão qa8", "Kinh dị");
        var second = await SeedMovieAsync(db, Guid.Parse("50000000-0000-0000-0000-000000000022"), "Giông qa8", "Kinh dị");
        var third = await SeedMovieAsync(db, Guid.Parse("f0000000-0000-0000-0000-000000000023"), "Lốc qa8", "Kinh dị");
        var soldOutShow = await SeedShowtimeAsync(db, soldOut.Id, room.Id);
        var soldBooking = await SeedBookingAsync(db, soldOutShow.Id, BookingStatus.Confirmed);
        await SeedShowtimeSeatAsync(db, soldOutShow, "A", 1, 100_000m, ShowtimeSeatStatus.Sold, soldBooking);
        await SeedShowtimeSeatAsync(db, soldOutShow, "A", 2, 100_000m, ShowtimeSeatStatus.Sold, soldBooking);
        var secondShow = await SeedShowtimeAsync(db, second.Id, room.Id, DateTime.UtcNow.AddDays(1).AddMinutes(150));
        await SeedShowtimeSeatAsync(db, secondShow, "A", 3, 100_000m);
        await SeedShowtimeSeatAsync(db, secondShow, "A", 4, 100_000m);
        await SeedShowtimeSeatAsync(db, secondShow, "A", 5, 100_000m);
        var thirdShow = await SeedShowtimeAsync(db, third.Id, room.Id, DateTime.UtcNow.AddDays(1).AddMinutes(300));
        await SeedShowtimeSeatAsync(db, thirdShow, "A", 6, 100_000m);
        await SeedShowtimeSeatAsync(db, thirdShow, "A", 7, 100_000m);
        await SeedShowtimeSeatAsync(db, thirdShow, "A", 8, 100_000m);
        var service = CreateService();
        var filters = new DiscoveryFilters { MovieNameText = "qa8", MinSeats = 3 };

        var output = await service.SearchAsync(CreateInput(filters), default);

        // The sold-out movie ranked first on keywords but offers no seat, so the others move up.
        Assert.Equal([second.Id, third.Id], output.Movies.Select(m => m.MovieId).ToList());
        Assert.DoesNotContain(output.Movies, m => m.MovieId == soldOut.Id);
    }

    private DiscoverySearchService CreateService() =>
        new(fixture.CreateDbContext(), TimeProvider.System, GeminiAiClients.Unconfigured,
            Options.Create(new GeminiOptions()), Options.Create(new DiscoveryConversationOptions()));

    private static DiscoverySearchInput CreateInput(DiscoveryFilters filters) =>
        new(Guid.NewGuid(), filters, "gợi ý phim đi", DateTime.UtcNow);

    private static async Task<(Cinema Cinema, Room Room)> SeedCinemaAsync(LumiCineDbContext db, string city = "Hà Nội")
    {
        var now = DateTime.UtcNow;
        var cinema = new Cinema(Guid.NewGuid(), $"C-{Guid.NewGuid():N}"[..22], $"Rạp {Guid.NewGuid():N}"[..12], "12 Bạch Đằng", city, now);
        var room = new Room(Guid.NewGuid(), cinema.Id, $"R-{Guid.NewGuid():N}"[..22], "Phòng một", now);
        await db.AddRangeAsync(cinema, room);
        await db.SaveChangesAsync();
        return (cinema, room);
    }

    private static async Task<Movie> SeedMovieAsync(LumiCineDbContext db, Guid movieId, string title, string genreName)
    {
        var now = DateTime.UtcNow;
        var movie = new Movie(movieId, title, 95, now, synopsis: $"Kể về {genreName.ToLowerInvariant()} qua nhiều thập kỷ.");
        // Genre names are unique per database; the hex suffix keeps them distinct without
        // colliding with the qa-marker query tokens used to scope candidates.
        var genre = new Genre(Guid.NewGuid(), $"{genreName} {Guid.NewGuid():N}", Guid.NewGuid().ToString("N"), now);
        var movieGenre = new MovieGenre(movie.Id, genre.Id, now);
        await db.AddRangeAsync(movie, genre, movieGenre);
        await db.SaveChangesAsync();
        return movie;
    }

    private static Task<Movie> SeedMovieAsync(LumiCineDbContext db, string title, string genreName) =>
        SeedMovieAsync(db, Guid.NewGuid(), title, genreName);

    private static async Task<Showtime> SeedShowtimeAsync(LumiCineDbContext db, Guid movieId, Guid roomId, DateTime? startUtc = null)
    {
        var start = startUtc ?? DateTime.UtcNow.AddDays(1);
        var showtime = new Showtime(Guid.NewGuid(), movieId, roomId, start, start.AddMinutes(100), 100_000m, DateTime.UtcNow);
        await db.Showtimes.AddAsync(showtime);
        await db.SaveChangesAsync();
        return showtime;
    }

    private static async Task<Booking> SeedBookingAsync(LumiCineDbContext db, Guid showtimeId, BookingStatus status, DateTime? holdExpiresAtUtc = null)
    {
        var booking = new Booking(Guid.NewGuid(), $"B-{Guid.NewGuid():N}", showtimeId, 500_000m, DateTime.UtcNow, status, holdExpiresAtUtc);
        await db.Bookings.AddAsync(booking);
        await db.SaveChangesAsync();
        return booking;
    }

    private static async Task<ShowtimeSeat> SeedShowtimeSeatAsync(
        LumiCineDbContext db,
        Showtime showtime,
        string rowLabel,
        int number,
        decimal price,
        ShowtimeSeatStatus status = ShowtimeSeatStatus.Available,
        Booking? booking = null)
    {
        var seat = new Seat(Guid.NewGuid(), showtime.RoomId, rowLabel, number, "Standard", DateTime.UtcNow);
        var showtimeSeat = status == ShowtimeSeatStatus.Held && booking is null
            ? new ShowtimeSeat(Guid.NewGuid(), showtime.Id, showtime.RoomId, seat.Id, price, DateTime.UtcNow, ShowtimeSeatStatus.Held)
            : new ShowtimeSeat(Guid.NewGuid(), showtime.Id, showtime.RoomId, seat.Id, price, DateTime.UtcNow);
        if (booking is not null)
            showtimeSeat.Hold(booking.Id);
        if (status == ShowtimeSeatStatus.Sold)
            showtimeSeat.Sell(booking!.Id);
        await db.AddRangeAsync(seat, showtimeSeat);
        await db.SaveChangesAsync();
        return showtimeSeat;
    }
}
