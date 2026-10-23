using LumiCine.Domain.Common;

namespace LumiCine.Domain.Bookings;

public sealed class CheckInLog
{
    private CheckInLog() { }

    public CheckInLog(
        Guid id,
        Guid staffUserId,
        Guid requestId,
        DateTime loggedAtUtc,
        CheckInLogResult result,
        Guid? ticketId = null,
        Guid? bookingId = null,
        Guid? showtimeId = null,
        string? attemptedTicketCode = null)
    {
        Id = EntityGuard.Id(id, nameof(id));
        StaffUserId = EntityGuard.Id(staffUserId, nameof(staffUserId));
        RequestId = EntityGuard.Id(requestId, nameof(requestId));
        LoggedAtUtc = EntityGuard.Utc(loggedAtUtc, nameof(loggedAtUtc));
        Result = EntityGuard.Defined(result, nameof(result));
        TicketId = ticketId is null ? null : EntityGuard.Id(ticketId.Value, nameof(ticketId));
        BookingId = bookingId is null ? null : EntityGuard.Id(bookingId.Value, nameof(bookingId));
        ShowtimeId = showtimeId is null ? null : EntityGuard.Id(showtimeId.Value, nameof(showtimeId));
        AttemptedTicketCode = EntityGuard.OptionalText(attemptedTicketCode, 60, nameof(attemptedTicketCode));
        if (result == CheckInLogResult.Success && TicketId is null)
            throw new ArgumentException("A successful check-in must reference its ticket.", nameof(ticketId));
    }

    public Guid Id { get; private set; }
    public Guid StaffUserId { get; private set; }
    public Guid RequestId { get; private set; }
    public Guid? TicketId { get; private set; }
    public Guid? BookingId { get; private set; }
    public Guid? ShowtimeId { get; private set; }
    public string? AttemptedTicketCode { get; private set; }
    public CheckInLogResult Result { get; private set; }
    public DateTime LoggedAtUtc { get; private set; }
}
