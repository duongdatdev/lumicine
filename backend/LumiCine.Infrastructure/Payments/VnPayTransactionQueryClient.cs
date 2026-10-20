using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LumiCine.Application.Common;
using LumiCine.Application.Payments;
using Microsoft.Extensions.Options;

namespace LumiCine.Infrastructure.Payments;

public sealed class VnPayTransactionQueryClient(HttpClient http, IOptions<VnPayOptions> options, TimeProvider clock) : IVnPayTransactionQueryClient
{
    private static readonly TimeZoneInfo VietnamTimeZone = TimeZoneInfo.CreateCustomTimeZone(
        "LumiCine-Vietnam", TimeSpan.FromHours(7), "Vietnam Standard Time", "Vietnam Standard Time");

    private readonly VnPayOptions _options = options.Value;

    public async Task<VnPayTransactionQueryResult> QueryAsync(
        string transactionReference, decimal amount, DateTime transactionDateUtc, CancellationToken ct)
    {
        if (!_options.IsConfigured())
            throw new ApiException(503, "VNPay payments are not configured.");

        var requestId = Guid.NewGuid().ToString("D");
        var now = FormatVietnamTime(clock.GetUtcNow().UtcDateTime);
        var transactionDate = FormatVietnamTime(transactionDateUtc);
        var orderInfo = $"Kiem tra giao dich {transactionReference}";
        var data = new Dictionary<string, string>
        {
            ["vnp_RequestId"] = requestId,
            ["vnp_Version"] = "2.1.0",
            ["vnp_Command"] = "querydr",
            ["vnp_TmnCode"] = _options.TmnCode,
            ["vnp_TxnRef"] = transactionReference,
            ["vnp_OrderInfo"] = orderInfo,
            ["vnp_TransactionDate"] = transactionDate,
            ["vnp_CreateDate"] = now,
            ["vnp_IpAddr"] = "127.0.0.1"
        };
        // The querydr checksum signs the raw field values joined with "|", per the VNPay
        // querydr/refund specification.
        var checksumInput = string.Join("|", requestId, "2.1.0", "querydr", _options.TmnCode,
            transactionReference, orderInfo, transactionDate, now, "127.0.0.1");
        data["vnp_SecureHash"] = Sign(checksumInput);

        using var response = await http.PostAsJsonAsync(_options.ApiBaseUrl, data, ct);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var fields = document.RootElement.EnumerateObject()
            .Where(property => property.Name.StartsWith("vnp_", StringComparison.Ordinal))
            .ToDictionary(property => property.Name, property => property.Value.ToString(), StringComparer.Ordinal);

        var responseChecksumInput = string.Join("|",
            fields.GetValueOrDefault("vnp_ResponseId"),
            fields.GetValueOrDefault("vnp_Command"),
            fields.GetValueOrDefault("vnp_TmnCode"),
            fields.GetValueOrDefault("vnp_TxnRef"),
            fields.GetValueOrDefault("vnp_Amount"),
            fields.GetValueOrDefault("vnp_BankCode"),
            fields.GetValueOrDefault("vnp_PayDate"),
            fields.GetValueOrDefault("vnp_TransactionNo"),
            fields.GetValueOrDefault("vnp_ResponseCode"),
            fields.GetValueOrDefault("vnp_Message"),
            fields.GetValueOrDefault("vnp_TransactionStatus"));
        var expectedHash = Sign(responseChecksumInput);
        if (!string.Equals(fields.GetValueOrDefault("vnp_SecureHash"), expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new VnPayCallbackException(VnPayResponseCodes.UnknownError, "The query response signature does not match.");

        return new VnPayTransactionQueryResult(
            fields.GetValueOrDefault("vnp_ResponseCode") ?? VnPayResponseCodes.UnknownError,
            fields.GetValueOrDefault("vnp_Message") ?? string.Empty,
            fields.GetValueOrDefault("vnp_TransactionNo"),
            fields.GetValueOrDefault("vnp_TransactionStatus"),
            long.TryParse(fields.GetValueOrDefault("vnp_Amount"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var amountMinor)
                ? amountMinor
                : null);
    }

    private string Sign(string input)
    {
        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(_options.HashSecret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
    }

    private static string FormatVietnamTime(DateTime utc) =>
        TimeZoneInfo.ConvertTime(utc, VietnamTimeZone).ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
}
