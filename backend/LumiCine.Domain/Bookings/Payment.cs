using LumiCine.Domain.Common;

namespace LumiCine.Domain.Bookings;

public sealed class Payment
{
    private Payment() { }

    public Payment(
        Guid id,
        Guid bookingId,
        Guid requestId,
        string merchantReference,
        decimal amount,
        string currency,
        DateTime expiresAtUtc,
        DateTime createdAtUtc,
        PaymentStatus status = PaymentStatus.Pending,
        PaymentProcessingStatus processingStatus = PaymentProcessingStatus.None)
    {
        Id = EntityGuard.Id(id, nameof(id));
        BookingId = EntityGuard.Id(bookingId, nameof(bookingId));
        RequestId = EntityGuard.Id(requestId, nameof(requestId));
        MerchantReference = EntityGuard.RequiredText(merchantReference, 100, nameof(merchantReference));
        Amount = EntityGuard.NonNegative(amount, nameof(amount));
        Currency = EntityGuard.RequiredText(currency, 10, nameof(currency));
        ExpiresAtUtc = EntityGuard.Utc(expiresAtUtc, nameof(expiresAtUtc));
        CreatedAtUtc = EntityGuard.Utc(createdAtUtc, nameof(createdAtUtc));
        Status = EntityGuard.Defined(status, nameof(status));
        ProcessingStatus = EntityGuard.Defined(processingStatus, nameof(processingStatus));
        if (Status == PaymentStatus.Succeeded && ProcessingStatus == PaymentProcessingStatus.None)
            throw new ArgumentException("A succeeded payment must record whether its money was used.", nameof(processingStatus));
    }

    public Guid Id { get; private set; }
    public Guid BookingId { get; private set; }
    public Guid RequestId { get; private set; }
    public string MerchantReference { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; private set; }
    public PaymentStatus Status { get; private set; }
    public PaymentProcessingStatus ProcessingStatus { get; private set; }
    public string? VnPayTransactionId { get; private set; }
    public string? ResponseCode { get; private set; }
    public string? TransactionStatus { get; private set; }
    public string? PaymentUrl { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ProcessedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    public void SetPaymentUrl(string paymentUrl, DateTime nowUtc)
    {
        PaymentUrl = EntityGuard.RequiredText(paymentUrl, 1000, nameof(paymentUrl));
        UpdatedAtUtc = EntityGuard.Utc(nowUtc, nameof(nowUtc));
    }

    public void MarkSucceeded(
        DateTime nowUtc,
        string? vnPayTransactionId,
        string? responseCode,
        string? transactionStatus,
        PaymentProcessingStatus processingStatus)
    {
        nowUtc = EntityGuard.Utc(nowUtc, nameof(nowUtc));
        if (Status == PaymentStatus.Succeeded)
            throw new InvalidOperationException("A succeeded payment cannot be applied again.");
        if (processingStatus is not (PaymentProcessingStatus.Fulfilled or PaymentProcessingStatus.RequiresRefundReview))
            throw new ArgumentException("A succeeded payment must be fulfilled or flagged for refund review.", nameof(processingStatus));

        Status = PaymentStatus.Succeeded;
        ProcessingStatus = processingStatus;
        VnPayTransactionId = OptionalText(vnPayTransactionId, 64, nameof(vnPayTransactionId));
        ResponseCode = OptionalText(responseCode, 16, nameof(responseCode));
        TransactionStatus = OptionalText(transactionStatus, 16, nameof(transactionStatus));
        ProcessedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public void MarkFailed(DateTime nowUtc, string? responseCode, string? transactionStatus)
    {
        nowUtc = EntityGuard.Utc(nowUtc, nameof(nowUtc));
        if (Status == PaymentStatus.Succeeded)
            throw new InvalidOperationException("A succeeded payment cannot fail.");

        Status = PaymentStatus.Failed;
        ResponseCode = OptionalText(responseCode, 16, nameof(responseCode));
        TransactionStatus = OptionalText(transactionStatus, 16, nameof(transactionStatus));
        ProcessedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    private static string? OptionalText(string? value, int maxLength, string name) => value is null || value.Length <= maxLength
        ? value
        : throw new ArgumentOutOfRangeException(name, $"Must be at most {maxLength} characters.");
}
