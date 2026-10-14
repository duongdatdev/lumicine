using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LumiCine.Application.Common;
using LumiCine.Application.Payments;
using Microsoft.Extensions.Options;

namespace LumiCine.Infrastructure.Payments;

public sealed class VnPayMessageService(IOptions<VnPayOptions> options) : IVnPayMessageService
{
    private const string Version = "2.1.0";
    private const string PayCommand = "pay";
    private const string Currency = "vnd";
    private const string Locale = "vn";
    private const string OrderType = "other";
    private const long AmountMinorLimit = 999_999_999_999;
    private static readonly TimeZoneInfo VietnamTimeZone = TimeZoneInfo.CreateCustomTimeZone(
        "LumiCine-Vietnam", TimeSpan.FromHours(7), "Vietnam Standard Time", "Vietnam Standard Time");

    private readonly VnPayOptions _options = options.Value;

    public string BuildPaymentUrl(VnPayPaymentRequest request)
    {
        EnsureConfigured();
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TransactionReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OrderInfo);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ClientIpAddress);

        var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["vnp_Version"] = Version,
            ["vnp_Command"] = PayCommand,
            ["vnp_TmnCode"] = _options.TmnCode,
            ["vnp_Amount"] = ToMinorUnits(request.Amount).ToString(CultureInfo.InvariantCulture),
            ["vnp_CreateDate"] = FormatVietnamTime(request.CreatedAtUtc),
            ["vnp_ExpireDate"] = FormatVietnamTime(request.ExpiresAtUtc),
            ["vnp_Currency"] = Currency,
            ["vnp_IpAddr"] = request.ClientIpAddress,
            ["vnp_Locale"] = Locale,
            ["vnp_OrderInfo"] = request.OrderInfo,
            ["vnp_OrderType"] = OrderType,
            ["vnp_ReturnUrl"] = _options.ReturnUrl,
            ["vnp_TxnRef"] = request.TransactionReference
        };
        var query = BuildQuery(parameters);
        return $"{_options.PaymentBaseUrl}?{query}&vnp_SecureHash={Sign(query)}";
    }

    public VnPayCallbackData ValidateCallback(IReadOnlyDictionary<string, IReadOnlyList<string>> parameters)
    {
        EnsureConfigured();
        ArgumentNullException.ThrowIfNull(parameters);
        var values = RequireSingleValues(parameters);
        var providedHash = RequireField(values, "vnp_SecureHash");
        if (providedHash.Length != 128 || !IsHexadecimal(providedHash))
            throw InvalidSignature("The secure hash is malformed.");

        var query = BuildQuery(BuildSignatureSource(values));
        var expectedHash = Sign(query);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expectedHash),
                Encoding.UTF8.GetBytes(providedHash.ToLowerInvariant())))
            throw InvalidSignature("The secure hash does not match the callback parameters.");

        if (!string.Equals(values["vnp_TmnCode"], _options.TmnCode, StringComparison.Ordinal))
            throw InvalidSignature("The callback merchant does not match this merchant.");

        var amount = RequireLong(values, "vnp_Amount");
        if (amount is not > 0)
            throw InvalidSignature("The callback amount is invalid.");
        var payDate = RequireField(values, "vnp_PayDate");
        if (payDate.Length != 14 || !IsNumeric(payDate))
            throw InvalidSignature("The callback pay date is malformed.");

        return new VnPayCallbackData(
            RequireField(values, "vnp_TxnRef"),
            amount,
            RequireField(values, "vnp_ResponseCode"),
            values.TryGetValue("vnp_TransactionStatus", out var transactionStatus) ? transactionStatus : string.Empty,
            RequireField(values, "vnp_TransactionNo"),
            payDate,
            values.TryGetValue("vnp_BankCode", out var bankCode) ? bankCode : null);
    }

    private static SortedDictionary<string, string> BuildSignatureSource(Dictionary<string, string> values)
    {
        var source = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in values)
        {
            if (!key.StartsWith("vnp_", StringComparison.Ordinal) ||
                key is "vnp_SecureHash" or "vnp_SecureHashType")
                continue;
            source[key] = value;
        }
        return source;
    }

    private static string BuildQuery(SortedDictionary<string, string> parameters) => string.Join("&",
        parameters.Select(pair => $"{pair.Key}={Uri.EscapeDataString(pair.Value)}"));

    private string Sign(string query)
    {
        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(_options.HashSecret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(query))).ToLowerInvariant();
    }

    private Dictionary<string, string> RequireSingleValues(IReadOnlyDictionary<string, IReadOnlyList<string>> parameters)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, list) in parameters)
        {
            if (list.Count != 1)
                throw InvalidSignature("Repeated parameters are not allowed.");
            values[key] = list[0];
        }
        return values;
    }

    private static string RequireField(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw InvalidSignature($"The callback is missing {key}.");

    private static long RequireLong(Dictionary<string, string> values, string key)
    {
        var raw = RequireField(values, key);
        return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw InvalidSignature($"The callback field {key} is malformed.");
    }

    private static long ToMinorUnits(decimal amount)
    {
        var minor = amount * 100;
        if (minor < 1 || minor > AmountMinorLimit || minor != decimal.Truncate(minor))
            throw new ApiException(400, "The booking total is outside the amount range VNPay accepts.");
        return (long)minor;
    }

    private void EnsureConfigured()
    {
        if (!_options.IsConfigured())
            throw new ApiException(503, "VNPay payments are not configured.");
    }

    private static VnPayCallbackException InvalidSignature(string message) =>
        new(VnPayResponseCodes.InvalidSignature, message);

    private static string FormatVietnamTime(DateTime utc) =>
        TimeZoneInfo.ConvertTime(utc, VietnamTimeZone).ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);

    private static bool IsHexadecimal(string value) => value.All(char.IsAsciiHexDigit);

    private static bool IsNumeric(string value) => value.All(char.IsAsciiDigit);
}
