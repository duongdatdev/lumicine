using LumiCine.Application.Common;
using System.Text.Json.Serialization;

namespace LumiCine.Application.Scheduling;

public sealed record ShowtimeRequest(
    [property: JsonRequired] Guid MovieId,
    [property: JsonRequired] Guid RoomId,
    [property: JsonRequired] string StartAt,
    [property: JsonRequired] decimal BasePrice);
public sealed record RescheduleShowtimeRequest(
    [property: JsonRequired] string StartAt,
    [property: JsonRequired] decimal BasePrice);
public sealed record ShowtimeSummary(Guid Id, Guid MovieId, string MovieTitle, Guid CinemaId, string CinemaName, Guid RoomId, string RoomName, DateTime StartAtUtc, DateTime EndAtUtc, decimal BasePrice, string Status);
public sealed record ShowtimeSeatSummary(Guid SeatId, string RowLabel, int Number, string SeatType, string Status, decimal Price);
public sealed record ShowtimeDetails(ShowtimeSummary Showtime, IReadOnlyList<ShowtimeSeatSummary> Seats);
public sealed record ShowtimeQuery(Guid? MovieId, Guid? CinemaId, DateOnly? Date, PageQuery Paging);

public interface IShowtimeService
{
    Task<PagedResult<ShowtimeSummary>> GetPublicAsync(ShowtimeQuery query, CancellationToken cancellationToken);
    Task<ShowtimeSummary?> GetPublicAsync(Guid id, CancellationToken cancellationToken);
    Task<PagedResult<ShowtimeSummary>> GetManagementAsync(ShowtimeQuery query, CancellationToken cancellationToken);
    Task<ShowtimeSummary?> GetManagementAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ShowtimeSeatSummary>?> GetSeatsAsync(Guid id, bool management, CancellationToken cancellationToken);
    Task<ShowtimeSummary> CreateAsync(ShowtimeRequest request, CancellationToken cancellationToken);
    Task<ShowtimeSummary> CreateWithIdAsync(Guid id, ShowtimeRequest request, CancellationToken cancellationToken);
    Task<ShowtimeSummary?> RescheduleAsync(Guid id, RescheduleShowtimeRequest request, CancellationToken cancellationToken);
    Task<bool> CancelAsync(Guid id, CancellationToken cancellationToken);
}
