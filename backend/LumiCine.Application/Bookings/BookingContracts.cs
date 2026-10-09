using System.Text.Json.Serialization;
using LumiCine.Application.Common;

namespace LumiCine.Application.Bookings;

public sealed record CreateBookingRequest(
    [property: JsonRequired] Guid RequestId,
    [property: JsonRequired] Guid ShowtimeId,
    [property: JsonRequired] IReadOnlyList<Guid> SeatIds);

public sealed record BookingQuery(PageQuery Paging, string? Status = null);

public sealed record BookingSeat(Guid SeatId, string RowLabel, int Number, string SeatType, decimal UnitPrice);

public sealed record BookingDetails(
    Guid Id,
    string Code,
    Guid ShowtimeId,
    Guid MovieId,
    string MovieTitle,
    Guid CinemaId,
    string CinemaName,
    Guid RoomId,
    string RoomName,
    string Status,
    DateTime? HoldExpiresAtUtc,
    decimal TotalAmount,
    DateTime CreatedAtUtc,
    DateTime ServerTimeUtc,
    IReadOnlyList<BookingSeat> Seats);

public interface IBookingService
{
    Task<(BookingDetails Booking, bool Created)> CreateAsync(Guid userId, CreateBookingRequest request, CancellationToken cancellationToken);
    Task<PagedResult<BookingDetails>> ListAsync(Guid userId, BookingQuery query, CancellationToken cancellationToken);
    Task<BookingDetails?> GetAsync(Guid userId, Guid bookingId, CancellationToken cancellationToken);
    Task CancelAsync(Guid userId, Guid bookingId, CancellationToken cancellationToken);
    Task<int> ExpireDueAsync(int batchSize, CancellationToken cancellationToken);
}
