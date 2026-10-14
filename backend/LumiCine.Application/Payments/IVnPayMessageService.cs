namespace LumiCine.Application.Payments;

public static class VnPayResponseCodes
{
    public const string Success = "00";
    public const string TransactionNotFound = "01";
    public const string InvalidAmount = "02";
    public const string AlreadyProcessed = "04";
    public const string InvalidSignature = "97";
    public const string UnknownError = "99";
}

public sealed class VnPayCallbackException(string rspCode, string message) : Exception(message)
{
    public string RspCode { get; } = rspCode;
}

public sealed record VnPayPaymentRequest(
    string TransactionReference,
    string OrderInfo,
    decimal Amount,
    DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc,
    string ClientIpAddress);

public sealed record VnPayCallbackData(
    string TransactionReference,
    long AmountMinor,
    string ResponseCode,
    string TransactionStatus,
    string TransactionId,
    string PayDate,
    string? BankCode);

public interface IVnPayMessageService
{
    string BuildPaymentUrl(VnPayPaymentRequest request);
    VnPayCallbackData ValidateCallback(IReadOnlyDictionary<string, IReadOnlyList<string>> parameters);
}
