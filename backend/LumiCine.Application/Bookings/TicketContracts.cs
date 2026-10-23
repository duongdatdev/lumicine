namespace LumiCine.Application.Bookings;

public sealed record TicketDetails(
    Guid Id,
    Guid BookingId,
    string BookingCode,
    string TicketCode,
    string Status,
    DateTime IssuedAtUtc,
    DateTime? UsedAtUtc,
    Guid ShowtimeId,
    string MovieTitle,
    string CinemaName,
    string RoomName,
    DateTime StartAtUtc,
    DateTime EndAtUtc,
    string RowLabel,
    int Number,
    string SeatType,
    decimal UnitPrice);

public sealed record TicketQrImage(byte[] Image);

public interface ITicketQrRenderer
{
    byte[] RenderPng(string ticketCode);
}

public interface ITicketService
{
    Task<IReadOnlyList<TicketDetails>> ListForBookingAsync(Guid userId, Guid bookingId, CancellationToken cancellationToken);
    Task<TicketDetails> GetOwnedAsync(Guid userId, Guid ticketId, CancellationToken cancellationToken);
    Task<TicketQrImage> GetQrImageAsync(Guid userId, Guid ticketId, CancellationToken cancellationToken);
}

public sealed record CheckInRequest(Guid RequestId, string TicketCode);

public sealed record CheckInResult(
    Guid RequestId,
    string Outcome,
    Guid? TicketId,
    string? TicketCode,
    Guid? BookingId,
    string? BookingCode,
    Guid? ShowtimeId,
    DateTime? UsedAtUtc,
    DateTime? WindowStartsAtUtc,
    DateTime? WindowEndsAtUtc,
    DateTime ServerTimeUtc);

public static class CheckInOutcomes
{
    public const string Success = "Success";
    public const string DuplicateScan = "DuplicateScan";
    public const string TicketNotFound = "TicketNotFound";
    public const string BookingNotConfirmed = "BookingNotConfirmed";
    public const string ShowtimeCancelled = "ShowtimeCancelled";
    public const string OutsideWindow = "OutsideWindow";
    public const string TicketVoided = "TicketVoided";
}

public interface ICheckInService
{
    Task<CheckInResult> CheckInAsync(Guid staffUserId, CheckInRequest request, CancellationToken cancellationToken);
}
