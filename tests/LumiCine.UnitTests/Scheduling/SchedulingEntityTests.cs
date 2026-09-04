using LumiCine.Domain.Scheduling;

namespace LumiCine.UnitTests.Scheduling;

public sealed class SchedulingEntityTests
{
    [Fact]
    public void Showtime_requires_positive_duration_and_nonnegative_price()
    {
        var now = DateTime.UtcNow;
        Assert.Throws<ArgumentException>(() => new Showtime(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now, now, 100, now));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Showtime(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now, now.AddHours(2), -1, now));
    }

    [Fact]
    public void Showtime_requires_utc_timestamps_and_a_defined_status()
    {
        var now = DateTime.UtcNow;
        Assert.Throws<ArgumentException>(() => new Showtime(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.Now, now.AddHours(2), 100, now));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Showtime(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now, now.AddHours(2), 100, now, (ShowtimeStatus)99));
    }

    [Fact]
    public void Showtime_seat_requires_nonnegative_price_and_valid_ids()
    {
        Assert.Throws<ArgumentException>(() => new ShowtimeSeat(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), 100, DateTime.UtcNow));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShowtimeSeat(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), -1, DateTime.UtcNow));
    }

    [Fact]
    public void Showtime_and_seat_accept_defaults_and_zero_price()
    {
        var now = DateTime.UtcNow;
        var showtime = new Showtime(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now, now.AddHours(2), 0, now);
        var seat = new ShowtimeSeat(Guid.NewGuid(), showtime.Id, showtime.RoomId, Guid.NewGuid(), 0, now);

        Assert.Equal(ShowtimeStatus.Scheduled, showtime.Status);
        Assert.Equal(ShowtimeSeatStatus.Available, seat.Status);
        Assert.Equal(0, seat.Price);
    }
}