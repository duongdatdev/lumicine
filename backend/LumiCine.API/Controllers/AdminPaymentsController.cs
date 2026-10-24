using LumiCine.Application.Accounts;
using LumiCine.Application.Common;
using LumiCine.Application.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LumiCine.API.Controllers;

[ApiController]
[Authorize(Roles = RoleNames.Admin)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/admin/payments")]
public sealed class AdminPaymentsController(IPaymentService payments) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<AdminPaymentView>>(StatusCodes.Status200OK)]
    public Task<PagedResult<AdminPaymentView>> List([FromQuery] Guid? bookingId, [FromQuery] string? status,
        [FromQuery] string? processingStatus, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        payments.ListForAdminAsync(new AdminPaymentQuery(new PageQuery(page, pageSize), bookingId, status, processingStatus), cancellationToken);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<AdminPaymentView>(StatusCodes.Status200OK)]
    public Task<AdminPaymentView> Get(Guid id, CancellationToken cancellationToken) =>
        payments.GetForAdminAsync(id, cancellationToken);

    [HttpPost("{id:guid}/reconcile")]
    [ProducesResponseType<VnPayReconcileResult>(StatusCodes.Status200OK)]
    public Task<VnPayReconcileResult> Reconcile(Guid id, CancellationToken cancellationToken) =>
        payments.ReconcileAsync(id, cancellationToken);
}
