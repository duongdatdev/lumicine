namespace LumiCine.Domain.Bookings;

public enum PaymentProcessingStatus
{
    None,
    Fulfilled,
    RequiresRefundReview,
    RefundRecorded
}
