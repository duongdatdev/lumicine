using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LumiCine.Domain.Bookings;

namespace LumiCine.IntegrationTests.Payments;

internal static class VnPayCallbacks
{
    internal const string HashSecret = "test-secret-value-0123456789";
    internal const string TmnCode = "LUMITEST";

    internal static Dictionary<string, IReadOnlyList<string>> SignedCallbackParameters(
        Payment payment,
        long? amountMinor = null,
        string responseCode = "00",
        string transactionStatus = "00") =>
        SignedCallbackParameters(payment.MerchantReference, amountMinor ?? (long)(payment.Amount * 100), responseCode, transactionStatus,
            transactionNo: payment.Id.ToString("N")[..16]);

    internal static Dictionary<string, IReadOnlyList<string>> SignedCallbackParameters(
        string merchantReference,
        long amountMinor,
        string responseCode = "00",
        string transactionStatus = "00",
        string? transactionNo = null)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["vnp_Amount"] = amountMinor.ToString(CultureInfo.InvariantCulture),
            ["vnp_BankCode"] = "NCB",
            ["vnp_Command"] = "pay",
            ["vnp_CreateDate"] = "20261113204500",
            ["vnp_Currency"] = "vnd",
            ["vnp_IpAddr"] = "127.0.0.1",
            ["vnp_Locale"] = "vn",
            ["vnp_OrderInfo"] = "Ve xem phim",
            ["vnp_OrderType"] = "other",
            ["vnp_PayDate"] = "20261113204600",
            ["vnp_ResponseCode"] = responseCode,
            ["vnp_TmnCode"] = TmnCode,
            ["vnp_TransactionNo"] = transactionNo ?? Guid.NewGuid().ToString("N")[..16],
            ["vnp_TransactionStatus"] = transactionStatus,
            ["vnp_TxnRef"] = merchantReference,
            ["vnp_Version"] = "2.1.0"
        };
        var query = string.Join("&", values.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(HashSecret));
        var hash = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(query))).ToLowerInvariant();
        var parameters = values.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)[p.Value]);
        parameters["vnp_SecureHash"] = [hash];
        return parameters;
    }
}
