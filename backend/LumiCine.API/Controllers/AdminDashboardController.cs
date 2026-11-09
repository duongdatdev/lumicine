using LumiCine.Application.Accounts;
using LumiCine.Application.Reporting;
using LumiCine.API.Reporting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LumiCine.API.Controllers;

[ApiController]
[Route("api/admin/dashboard")]
[Authorize(Roles = RoleNames.Admin)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminDashboardController(IAdminReportingService reports) : ControllerBase
{
    [HttpGet("summary")]
    public Task<AdminDashboardSummary> Summary([FromQuery] ReportFilter filter, CancellationToken cancellationToken)
    {
        ReportDateQueryValidation.Validate(Request, filter);
        return reports.GetSummaryAsync(filter, cancellationToken);
    }

    [HttpGet("operations")]
    public Task<AdminDashboardOperations> Operations([FromQuery] Guid? movieId, [FromQuery] Guid? cinemaId,
        CancellationToken cancellationToken)
    {
        return reports.GetOperationsAsync(new ReportFilter(MovieId: movieId, CinemaId: cinemaId), cancellationToken);
    }
}
