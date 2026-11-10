using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json.Serialization;
using LumiCine.Application.Common;
using LumiCine.Application.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LumiCine.API.Controllers;

[ApiController]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/payments")]
public sealed class PaymentsController(IPaymentService payments) : ControllerBase
{
    [HttpGet("{id:guid}")]
    [Authorize]
    [ProducesResponseType<PaymentView>(StatusCodes.Status200OK)]
    public Task<PaymentView> Get(Guid id, CancellationToken cancellationToken) =>
        payments.GetOwnedAsync(UserId, id, cancellationToken);

    [HttpGet("vnpay/ipn")]
    [AllowAnonymous]
    public async Task<IActionResult> VnPayInstantPaymentNotification(CancellationToken cancellationToken)
    {
        var outcome = await ApplyCallbackSafelyAsync(cancellationToken);
        // VNPay requires the exact RspCode/Message JSON contract, so keep the property casing.
        return Ok(new VnPayIpnResponse(outcome.RspCode, outcome.Message));
    }

    [HttpGet("vnpay/return")]
    [AllowAnonymous]
    public async Task<IActionResult> VnPayReturn(CancellationToken cancellationToken)
    {
        var outcome = await payments.ProcessVnPayReturnAsync(CollectCallbackParameters(), cancellationToken);
        if (outcome.RspCode == VnPayResponseCodes.InvalidSignature)
            throw new ApiException(400, outcome.Message);
        if (outcome.RspCode == VnPayResponseCodes.TransactionNotFound)
            throw new ApiException(404, outcome.Message);
        return Ok(new VnPayReturnResponse(outcome.RspCode, outcome.Message, outcome.Payment));
    }

    private async Task<VnPayCallbackOutcome> ApplyCallbackSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await payments.ApplyVnPayCallbackAsync(CollectCallbackParameters(), cancellationToken);
        }
        catch (ApiException)
        {
            // The callback result itself could not be committed (for example a concurrent
            // change); VNPay only understands the RspCode protocol here.
            return new VnPayCallbackOutcome(VnPayResponseCodes.UnknownError, "The payment result could not be applied.", null);
        }
    }

    private Dictionary<string, IReadOnlyList<string>> CollectCallbackParameters()
    {
        var parameters = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var key in Request.Query.Keys)
        {
            parameters[key] = Request.Query[key].Where(value => value is not null).Select(value => value!).ToArray();
        }
        return parameters;
    }

    private Guid UserId => Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
}

public sealed record VnPayIpnResponse(
    [property: JsonPropertyName("RspCode")] string RspCode,
    [property: JsonPropertyName("Message")] string Message);

public sealed record VnPayReturnResponse(
    string RspCode,
    string Message,
    PaymentView? Payment);
