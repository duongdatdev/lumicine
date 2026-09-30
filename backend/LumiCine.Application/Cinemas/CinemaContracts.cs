using LumiCine.Application.Common;

namespace LumiCine.Application.Cinemas;

public sealed record CinemaSummary(Guid Id, string Code, string Name, string Address, string City, bool IsActive);
public sealed record RoomSummary(Guid Id, Guid CinemaId, string Code, string Name, bool IsActive, int SeatCount);
public sealed record SeatSummary(Guid Id, Guid RoomId, string RowLabel, int Number, string SeatType, bool IsActive);
public sealed record CinemaRequest(
    [param: System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(30)] string Code,
    [param: System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(160)] string Name,
    [param: System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(300)] string Address,
    [param: System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(100)] string City);
public sealed record RoomRequest(
    [param: System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(30)] string Code,
    [param: System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(100)] string Name);
public sealed record SeatLayoutRow(string Label, int StandardSeats, int VipSeats = 0);
public sealed record SeatStatusRequest([param: System.ComponentModel.DataAnnotations.Required] bool? IsActive);
public sealed record CinemaStatusRequest([param: System.ComponentModel.DataAnnotations.Required] bool? IsActive);
public sealed record RoomStatusRequest([param: System.ComponentModel.DataAnnotations.Required] bool? IsActive);

public interface ICinemaService
{
    Task<PagedResult<CinemaSummary>> GetPublicCinemasAsync(PageQuery page, string? city, CancellationToken cancellationToken);
    Task<CinemaSummary?> GetPublicCinemaAsync(Guid id, CancellationToken cancellationToken);
    Task<PagedResult<CinemaSummary>> GetAdminCinemasAsync(PageQuery page, CancellationToken cancellationToken);
    Task<CinemaSummary?> GetAdminCinemaAsync(Guid id, CancellationToken cancellationToken);
    Task<Guid> CreateCinemaAsync(CinemaRequest request, CancellationToken cancellationToken);
    Task<bool> UpdateCinemaAsync(Guid id, CinemaRequest request, CancellationToken cancellationToken);
    Task<bool> SetCinemaActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken);
    Task<PagedResult<RoomSummary>?> GetAdminRoomsAsync(Guid cinemaId, PageQuery page, CancellationToken cancellationToken);
    Task<RoomSummary?> GetAdminRoomAsync(Guid id, CancellationToken cancellationToken);
    Task<PagedResult<SeatSummary>?> GetAdminSeatsAsync(Guid roomId, PageQuery page, CancellationToken cancellationToken);
    Task<Guid> CreateRoomAsync(Guid cinemaId, RoomRequest request, CancellationToken cancellationToken);
    Task<bool> UpdateRoomAsync(Guid id, RoomRequest request, CancellationToken cancellationToken);
    Task<bool> SetRoomActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken);
    Task<bool> ReplaceSeatLayoutAsync(Guid roomId, IReadOnlyList<SeatLayoutRow> rows, CancellationToken cancellationToken);
    Task<bool> SetSeatActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken);
}
