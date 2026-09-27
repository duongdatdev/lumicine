using LumiCine.Domain.Common;

namespace LumiCine.Domain.Cinemas;

public sealed class Seat
{
    private Seat() => RowLabel = SeatType = string.Empty;

    public Seat(
        Guid id,
        Guid roomId,
        string rowLabel,
        int number,
        string seatType,
        DateTime createdAtUtc,
        bool isActive = true)
    {
        Id = EntityGuard.Id(id, nameof(id));
        RoomId = EntityGuard.Id(roomId, nameof(roomId));
        RowLabel = EntityGuard.RequiredText(rowLabel.Trim().ToUpperInvariant(), 10, nameof(rowLabel));
        Number = EntityGuard.Positive(number, nameof(number));
        SeatType = NormalizeType(seatType);
        CreatedAtUtc = EntityGuard.Utc(createdAtUtc, nameof(createdAtUtc));
        IsActive = isActive;
    }

    public Guid Id { get; private set; }
    public Guid RoomId { get; private set; }
    public string RowLabel { get; private set; }
    public int Number { get; private set; }
    public string SeatType { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public void SetActive(bool isActive) => IsActive = isActive;

    private static string NormalizeType(string type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        return type.Trim().ToUpperInvariant() switch
        {
            "STANDARD" => "Standard",
            "VIP" => "VIP",
            _ => throw new ArgumentException("Seat type must be Standard or VIP.", nameof(type))
        };
    }
}
