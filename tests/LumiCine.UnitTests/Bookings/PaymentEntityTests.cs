using LumiCine.Domain.Bookings;

namespace LumiCine.UnitTests.Bookings;

public sealed class PaymentEntityTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    [Fact]
    public void Payment_requires_ids_reference_amount_currency_and_utc_times()
    {
        Assert.Throws<ArgumentException>(() => NewPayment(id: Guid.Empty));
        Assert.Throws<ArgumentException>(() => NewPayment(bookingId: Guid.Empty));
        Assert.Throws<ArgumentException>(() => NewPayment(requestId: Guid.Empty));
        Assert.Throws<ArgumentException>(() => NewPayment(merchantReference: " "));
        Assert.Throws<ArgumentOutOfRangeException>(() => NewPayment(amount: -1));
        Assert.Throws<ArgumentException>(() => NewPayment(currency: "  "));
        Assert.Throws<ArgumentException>(() => NewPayment(expiresAtUtc: DateTime.Now));
        Assert.Throws<ArgumentException>(() => NewPayment(createdAtUtc: DateTime.Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => NewPayment(merchantReference: new string('x', 101)));
    }

    [Fact]
    public void Payment_defaults_to_pending_and_records_vnpay_fields_on_success()
    {
        var payment = NewPayment();

        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal(PaymentProcessingStatus.None, payment.ProcessingStatus);
        Assert.Null(payment.VnPayTransactionId);
        Assert.Null(payment.ProcessedAtUtc);

        payment.SetPaymentUrl("https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?vnp_TxnRef=TX", Now);
        payment.MarkSucceeded(Now.AddMinutes(2), "987654", "00", "00", PaymentProcessingStatus.Fulfilled);

        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal(PaymentProcessingStatus.Fulfilled, payment.ProcessingStatus);
        Assert.Equal("987654", payment.VnPayTransactionId);
        Assert.Equal("00", payment.ResponseCode);
        Assert.Equal("00", payment.TransactionStatus);
        Assert.Equal(Now.AddMinutes(2), payment.ProcessedAtUtc);
        Assert.NotNull(payment.PaymentUrl);
    }

    [Fact]
    public void Succeeded_payment_requires_a_processing_status()
    {
        Assert.Throws<ArgumentException>(() => new Payment(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "REF-1", 120, "VND", Now, Now,
            PaymentStatus.Succeeded, PaymentProcessingStatus.None));
        Assert.Throws<ArgumentException>(() => NewPayment().MarkSucceeded(Now, "1", "00", "00", PaymentProcessingStatus.None));
        Assert.Throws<ArgumentException>(() => NewPayment().MarkSucceeded(Now, "1", "00", "00", PaymentProcessingStatus.RefundRecorded));
    }

    [Fact]
    public void Succeeded_payment_cannot_be_applied_or_failed_again()
    {
        var payment = NewPayment();
        payment.MarkSucceeded(Now, "1", "00", "00", PaymentProcessingStatus.Fulfilled);

        Assert.Throws<InvalidOperationException>(() =>
            payment.MarkSucceeded(Now, "1", "00", "00", PaymentProcessingStatus.RequiresRefundReview));
        Assert.Throws<InvalidOperationException>(() => payment.MarkFailed(Now, "07", "03"));
        Assert.Equal(PaymentProcessingStatus.Fulfilled, payment.ProcessingStatus);
    }

    [Fact]
    public void Failed_payment_can_record_failure_but_late_success_can_still_arrive()
    {
        var payment = NewPayment();
        payment.MarkFailed(Now, "24", "02");

        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Equal("24", payment.ResponseCode);
        Assert.Equal(Now, payment.ProcessedAtUtc);

        payment.MarkSucceeded(Now.AddMinutes(10), "55", "00", "00", PaymentProcessingStatus.RequiresRefundReview);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal(PaymentProcessingStatus.RequiresRefundReview, payment.ProcessingStatus);
    }

    [Fact]
    public void Failed_payment_requires_utc_processed_time()
    {
        Assert.Throws<ArgumentException>(() => NewPayment().MarkFailed(DateTime.Now, null, null));
    }

    private static Payment NewPayment(
        Guid? id = null,
        Guid? bookingId = null,
        Guid? requestId = null,
        string merchantReference = "REF-0001",
        decimal amount = 120,
        string currency = "VND",
        DateTime? expiresAtUtc = null,
        DateTime? createdAtUtc = null) =>
        new(id ?? Guid.NewGuid(),
            bookingId ?? Guid.NewGuid(),
            requestId ?? Guid.NewGuid(),
            merchantReference,
            amount,
            currency,
            expiresAtUtc ?? Now.AddMinutes(5),
            createdAtUtc ?? Now);
}
