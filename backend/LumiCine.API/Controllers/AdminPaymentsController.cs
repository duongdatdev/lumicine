using LumiCine.Application.Accounts;
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
    [HttpPost("{id:guid}/reconcile")]
    [ProducesResponseType<VnPayReconcileResult>(StatusCodes.Status200OK)]
    public Task<VnPayReconcileResult> Reconcile(Guid id, CancellationToken cancellationToken) =>
        payments.ReconcileAsync(id, cancellationToken);
}
