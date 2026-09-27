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

    [Fact]
    public void Cinema_and_room_trim_fields_and_normalize_codes()
    {
        var cinema = new Cinema(Guid.NewGuid(), " c-01 ", "  Lumi  ", " Main St ", " HCMC ", DateTime.UtcNow);
        var room = new Room(Guid.NewGuid(), cinema.Id, " r-02 ", " Room 2 ", DateTime.UtcNow);

        Assert.Equal("C-01", cinema.Code);
        Assert.Equal("Lumi", cinema.Name);
        Assert.Equal("R-02", room.Code);
        Assert.Equal("Room 2", room.Name);
    }

    [Theory]
    [InlineData("standard", "Standard")]
    [InlineData(" VIP ", "VIP")]
    public void Seat_normalizes_supported_types_and_rows(string input, string expected)
    {
        var seat = new Seat(Guid.NewGuid(), Guid.NewGuid(), " a ", 1, input, DateTime.UtcNow);

        Assert.Equal("A", seat.RowLabel);
        Assert.Equal(expected, seat.SeatType);
    }

    [Fact]
    public void Seat_rejects_unknown_types()
    {
        Assert.Throws<ArgumentException>(() => new Seat(Guid.NewGuid(), Guid.NewGuid(), "A", 1, "Recliner", DateTime.UtcNow));
    }

    [Fact]
    public void Updates_change_metadata_and_activation_without_exposing_setters()
    {
        var now = DateTime.UtcNow;
        var cinema = new Cinema(Guid.NewGuid(), "C1", "Old", "Address", "City", now);
        var room = new Room(Guid.NewGuid(), cinema.Id, "R1", "Old room", now);

        cinema.Update(" c2 ", " New ", " New address ", " HCMC ", now.AddMinutes(1));
        room.Update(" r2 ", " New room ", now.AddMinutes(1));
        cinema.SetActive(false, now.AddMinutes(2));
        room.SetActive(false, now.AddMinutes(2));

        Assert.Equal("C2", cinema.Code);
        Assert.Equal("New", cinema.Name);
        Assert.Equal("R2", room.Code);
        Assert.False(cinema.IsActive);
        Assert.False(room.IsActive);
    }
}
