using System.Globalization;
using System.Text;
using LumiCine.Application.Accounts;
using LumiCine.Application.Common;
using LumiCine.Application.Reporting;
using LumiCine.API.Reporting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LumiCine.API.Controllers;

[ApiController]
[Route("api/admin/reports")]
[Authorize(Roles = RoleNames.Admin)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminReportsController(IAdminReportingService reports) : ControllerBase
{
    [HttpGet("revenue")]
    public Task<AdminReportResult<RevenueReportRow, RevenueReportTotals>> Revenue([FromQuery] ReportFilter filter,
        [FromQuery] string groupBy = "day", CancellationToken cancellationToken = default)
    {
        ReportDateQueryValidation.Validate(Request, filter);
        if (!TryParseGroupBy(groupBy, out var parsed)) throw new ApiException(400, "groupBy must be day, movie, cinema, or showtime.");
        return reports.GetRevenueAsync(new RevenueReportFilter(filter.FromDate, filter.ToDate, filter.MovieId,
            filter.CinemaId, filter.Page, filter.PageSize, parsed), cancellationToken);
    }

    [HttpGet("showtimes")]
    public Task<AdminReportResult<ShowtimeReportRow, ShowtimeReportTotals>> Showtimes([FromQuery] ReportFilter filter, CancellationToken cancellationToken)
    {
        ReportDateQueryValidation.Validate(Request, filter);
        return reports.GetShowtimesAsync(filter, cancellationToken);
    }

    [HttpGet("revenue/export")]
    [EnableRateLimiting("AdminReportExportPerAdmin")]
    public async Task<IActionResult> ExportRevenue([FromQuery] ReportFilter filter,
        [FromQuery] string groupBy = "day", CancellationToken cancellationToken = default)
    {
        ReportDateQueryValidation.Validate(Request, filter);
        if (!TryParseGroupBy(groupBy, out var parsed)) throw new ApiException(400, "groupBy must be day, movie, cinema, or showtime.");
        var result = await reports.GetRevenueExportAsync(new RevenueReportFilter(filter.FromDate, filter.ToDate,
            filter.MovieId, filter.CinemaId, 1, 100, parsed), cancellationToken);
        if (result.Rows.Count > ReportExportLimits.MaxRows) return Problem(statusCode: 422, title: $"Export exceeds the {ReportExportLimits.MaxRows} row limit.");
        return Csv("revenue", result.Metadata, result.Rows.Count, writer => WriteRevenue(writer, result.Rows, cancellationToken), cancellationToken);
    }

    [HttpGet("showtimes/export")]
    [EnableRateLimiting("AdminReportExportPerAdmin")]
    public async Task<IActionResult> ExportShowtimes([FromQuery] ReportFilter filter, CancellationToken cancellationToken)
    {
        ReportDateQueryValidation.Validate(Request, filter);
        var result = await reports.GetShowtimesExportAsync(filter with { Page = 1, PageSize = 100 }, cancellationToken);
        if (result.Rows.Count > ReportExportLimits.MaxRows) return Problem(statusCode: 422, title: $"Export exceeds the {ReportExportLimits.MaxRows} row limit.");
        return Csv("showtimes", result.Metadata, result.Rows.Count, writer => WriteShowtimes(writer, result.Rows, cancellationToken), cancellationToken);
    }

    private IActionResult Csv(string type, ReportMetadata metadata, int count, Func<StreamWriter, Task> write, CancellationToken cancellationToken)
    {
        Response.Headers["X-Report-Generated-At"] = metadata.GeneratedAtUtc.ToString("O", CultureInfo.InvariantCulture);
        Response.Headers["X-Report-Row-Count"] = count.ToString(CultureInfo.InvariantCulture);
        var fromDate = metadata.FromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toDate = metadata.ToDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var filename = $"{type}-{fromDate}-to-{toDate}.csv";
        return new CsvResult(filename, async stream =>
        {
            await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), 1024, leaveOpen: true);
            writer.NewLine = "\r\n";
            await write(writer);
            await writer.FlushAsync(cancellationToken);
        });
    }

    private static async Task WriteRevenue(StreamWriter writer, IReadOnlyList<RevenueReportRow> rows, CancellationToken ct)
    {
        await CsvLine(writer, ct, "key", "label", "ticketSalesAmount", "collectedAmount", "recordedRefundAmount", "netCollectedAmount", "successfulPaymentCount", "failedPaymentCount");
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            await CsvLine(writer, ct, SafeText(row.Key), SafeText(row.Label), Money(row.TicketSalesAmount), Money(row.CollectedAmount),
                Money(row.RecordedRefundAmount), Money(row.NetCollectedAmount), Number(row.SuccessfulPaymentCount), Number(row.FailedPaymentCount));
        }
    }

    private static async Task WriteShowtimes(StreamWriter writer, IReadOnlyList<ShowtimeReportRow> rows, CancellationToken ct)
    {
        await CsvLine(writer, ct, "showtimeId", "movieId", "movieTitle", "cinemaId", "cinemaName", "roomId", "roomName", "startAt", "endAt", "status", "capacity", "soldSeats", "heldSeats", "availableSeats", "occupancyRate", "validTickets", "usedTickets", "checkInRate", "issuedTicketAmount");
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            await CsvLine(writer, ct, row.ShowtimeId.ToString(), row.MovieId.ToString(), SafeText(row.MovieTitle), row.CinemaId.ToString(), SafeText(row.CinemaName),
                row.RoomId.ToString(), SafeText(row.RoomName), OffsetTime(row.StartAtUtc), OffsetTime(row.EndAtUtc), SafeText(row.Status), Number(row.Capacity),
                Number(row.SoldSeats), Number(row.HeldSeats), Number(row.AvailableSeats), Money(row.OccupancyRate), Number(row.ValidTickets),
                Number(row.UsedTickets), Money(row.CheckInRate), Money(row.IssuedTicketAmount));
        }
    }

    private static Task CsvLine(StreamWriter writer, CancellationToken ct, params string[] fields) =>
        writer.WriteLineAsync(string.Join(',', fields.Select(EscapeCsv)).AsMemory(), ct);

    private static string EscapeCsv(string value)
    {
        return '"' + value.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';
    }

    private static string SafeText(string value)
    {
        var probe = value.AsSpan().TrimStart();
        return !probe.IsEmpty && probe[0] is '=' or '+' or '-' or '@' ? "'" + value : value;
    }

    private static string Money(decimal value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    private static string OffsetTime(DateTime value) => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)).ToOffset(TimeSpan.FromHours(7)).ToString("O", CultureInfo.InvariantCulture);

    private static bool TryParseGroupBy(string value, out RevenueGroupBy groupBy)
    {
        groupBy = value.ToLowerInvariant() switch
        {
            "day" => RevenueGroupBy.Day,
            "movie" => RevenueGroupBy.Movie,
            "cinema" => RevenueGroupBy.Cinema,
            "showtime" => RevenueGroupBy.Showtime,
            _ => (RevenueGroupBy)(-1)
        };
        return Enum.IsDefined(groupBy);
    }

    private sealed class CsvResult(string filename, Func<Stream, Task> write) : IActionResult
    {
        public async Task ExecuteResultAsync(ActionContext context)
        {
            var response = context.HttpContext.Response;
            response.ContentType = "text/csv; charset=utf-8";
            response.Headers.ContentDisposition = $"attachment; filename=\"{filename}\"";
            await write(response.Body);
        }
    }
}
