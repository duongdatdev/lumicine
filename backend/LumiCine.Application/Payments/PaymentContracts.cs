using LumiCine.Application.Common;

namespace LumiCine.Application.Payments;

public sealed record CreateBookingPaymentRequest(Guid RequestId);

public sealed record BookingPaymentLink(
    Guid BookingId,
    Guid PaymentId,
    Guid RequestId,
    string PaymentUrl,
    DateTime ExpiresAtUtc);

public sealed record PaymentView(
    Guid Id,
    Guid BookingId,
    Guid RequestId,
    string MerchantReference,
    decimal Amount,
    string Currency,
    string Status,
    string ProcessingStatus,
    string? PaymentUrl,
    DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc,
    DateTime? ProcessedAtUtc,
    string? VnPayTransactionId,
    string? ResponseCode,
    string? TransactionStatus);

public sealed record VnPayCallbackOutcome(string RspCode, string Message, PaymentView? Payment);

public sealed record VnPayReconcileResult(string RspCode, string Message, PaymentView Payment);

public sealed record AdminPaymentQuery(PageQuery Paging, Guid? BookingId = null, string? Status = null, string? ProcessingStatus = null);

public sealed record AdminPaymentView(PaymentView Payment, string BookingCode, string? CustomerEmail);

public sealed record RecordRefundRequest(string Reference, string? Note);

public interface IPaymentService
{
    Task<BookingPaymentLink> CreateBookingPaymentAsync(Guid userId, Guid bookingId, CreateBookingPaymentRequest request, string clientIpAddress, CancellationToken cancellationToken);
    Task<PaymentView> GetOwnedAsync(Guid userId, Guid paymentId, CancellationToken cancellationToken);
    Task<VnPayCallbackOutcome> ApplyVnPayCallbackAsync(IReadOnlyDictionary<string, IReadOnlyList<string>> parameters, CancellationToken cancellationToken);
    Task<VnPayReconcileResult> ReconcileAsync(Guid paymentId, CancellationToken cancellationToken);
    Task<PagedResult<AdminPaymentView>> ListForAdminAsync(AdminPaymentQuery query, CancellationToken cancellationToken);
    Task<AdminPaymentView> GetForAdminAsync(Guid paymentId, CancellationToken cancellationToken);
    Task<AdminPaymentView> RecordRefundAsync(Guid adminUserId, Guid paymentId, RecordRefundRequest request, CancellationToken cancellationToken);
}
