namespace LumiCine.Domain.Bookings;

public enum CheckInLogResult
{
    Success,
    DuplicateScan,
    TicketNotFound,
    BookingNotConfirmed,
    ShowtimeCancelled,
    OutsideWindow,
    TicketVoided
}
