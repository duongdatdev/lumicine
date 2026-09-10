using LumiCine.Domain.Common;

namespace LumiCine.Domain.Bookings;

public sealed class Booking
{
    private Booking() => Code = string.Empty;

    public Booking(
        Guid id,
        string code,
        Guid showtimeId,
        decimal totalAmount,
        DateTime createdAtUtc,
        BookingStatus status = BookingStatus.Pending,
        DateTime? holdExpiresAtUtc = null,
        DateTime? updatedAtUtc = null,
        Guid? userId = null)
    {
        Id = EntityGuard.Id(id, nameof(id));
        Code = EntityGuard.RequiredText(code, 40, nameof(code));
        ShowtimeId = EntityGuard.Id(showtimeId, nameof(showtimeId));
        Status = EntityGuard.Defined(status, nameof(status));
        HoldExpiresAtUtc = EntityGuard.OptionalUtc(holdExpiresAtUtc, nameof(holdExpiresAtUtc));
        TotalAmount = EntityGuard.NonNegative(totalAmount, nameof(totalAmount));
        CreatedAtUtc = EntityGuard.Utc(createdAtUtc, nameof(createdAtUtc));
        UpdatedAtUtc = EntityGuard.OptionalUtc(updatedAtUtc, nameof(updatedAtUtc));
        UserId = userId is null ? null : EntityGuard.Id(userId.Value, nameof(userId));
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; }
    public Guid ShowtimeId { get; private set; }
    public Guid? UserId { get; private set; }
    public BookingStatus Status { get; private set; }
    public DateTime? HoldExpiresAtUtc { get; private set; }
    public decimal TotalAmount { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }
}
