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
        Guid? userId = null,
        Guid? requestId = null)
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
        RequestId = requestId is null ? null : EntityGuard.Id(requestId.Value, nameof(requestId));
        if (RequestId is not null && (UserId is null || HoldExpiresAtUtc is null || HoldExpiresAtUtc <= CreatedAtUtc))
            throw new ArgumentException("A booking request requires an account and a deadline after creation.", nameof(requestId));
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; }
    public Guid ShowtimeId { get; private set; }
    public Guid? UserId { get; private set; }
    public Guid? RequestId { get; private set; }
    public BookingStatus Status { get; private set; }
    public DateTime? HoldExpiresAtUtc { get; private set; }
    public decimal TotalAmount { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    public void Cancel(DateTime nowUtc)
    {
        nowUtc = EntityGuard.Utc(nowUtc, nameof(nowUtc));
        if (Status != BookingStatus.Pending)
            throw new InvalidOperationException("Only pending bookings can be cancelled.");

        Status = BookingStatus.Cancelled;
        UpdatedAtUtc = nowUtc;
    }

    public void Expire(DateTime nowUtc)
    {
        nowUtc = EntityGuard.Utc(nowUtc, nameof(nowUtc));
        if (Status != BookingStatus.Pending || HoldExpiresAtUtc is null || nowUtc < HoldExpiresAtUtc)
            throw new InvalidOperationException("Only due pending bookings can expire.");

        Status = BookingStatus.Expired;
        UpdatedAtUtc = nowUtc;
    }
}
