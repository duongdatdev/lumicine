using LumiCine.Domain.Cinemas;

namespace LumiCine.UnitTests.Cinemas;

public sealed class CinemaEntityTests
{
    [Fact]
    public void Cinema_requires_all_identity_and_address_fields()
    {
        Assert.Throws<ArgumentException>(() => new Cinema(Guid.Empty, "C01", "Cinema", "Address", "City", DateTime.UtcNow));
        Assert.Throws<ArgumentException>(() => new Cinema(Guid.NewGuid(), "C01", "Cinema", " ", "City", DateTime.UtcNow));
    }

    [Fact]
    public void Room_requires_a_cinema_and_bounded_code()
    {
        Assert.Throws<ArgumentException>(() => new Room(Guid.NewGuid(), Guid.Empty, "R1", "Room", DateTime.UtcNow));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Room(Guid.NewGuid(), Guid.NewGuid(), new string('x', 31), "Room", DateTime.UtcNow));
    }

    [Fact]
    public void Seat_requires_a_row_label_and_positive_number()
    {
        Assert.Throws<ArgumentException>(() => new Seat(Guid.NewGuid(), Guid.NewGuid(), " ", 1, "Standard", DateTime.UtcNow));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Seat(Guid.NewGuid(), Guid.NewGuid(), "A", 0, "Standard", DateTime.UtcNow));
    }

    [Fact]
    public void Seat_accepts_number_one()
    {
        var seat = new Seat(Guid.NewGuid(), Guid.NewGuid(), "A", 1, "Standard", DateTime.UtcNow);

        Assert.Equal(1, seat.Number);
    }
}