using System.Security.Cryptography;
using System.Text;
using LumiCine.Application.Common;
using LumiCine.Application.Payments;
using LumiCine.Infrastructure.Payments;
using Microsoft.Extensions.Options;

namespace LumiCine.IntegrationTests.Payments;

public sealed class VnPayMessageServiceTests
{
    private static readonly VnPayOptions TestOptions = new()
    {
        TmnCode = "LUMITST",
        HashSecret = "test-secret-value-0123456789",
        ReturnUrl = "https://lumicine.example.test/payments/vnpay/return",
        IpnUrl = "https://lumicine.example.test/payments/vnpay/ipn"
    };

    private static readonly DateTime CreatedAtUtc = new(2026, 11, 13, 13, 45, 0, DateTimeKind.Utc);

    [Fact]
    public void Payment_url_is_sorted_encoded_and_multiplies_the_amount()
    {
        var url = CreateService().BuildPaymentUrl(new VnPayPaymentRequest(
            "LC123.AAAA", "Ve xem phim LC123", 120m, CreatedAtUtc, CreatedAtUtc.AddMinutes(5), "127.0.0.1"));

        Assert.StartsWith("https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?", url);
        var pairs = url[(url.IndexOf('?') + 1)..].Split('&').Select(p => p.Split('=', 2)).ToArray();
        Assert.Equal("vnp_SecureHash", pairs.Last()[0]);
        var keys = pairs[..^1].Select(p => p[0]).ToArray();
        Assert.Equal(keys.Order(StringComparer.Ordinal), keys);
        var map = pairs.ToDictionary(p => p[0], p => p[1]);

        Assert.Equal("12000", map["vnp_Amount"]);
        Assert.Equal("20261113204500", map["vnp_CreateDate"]);
        Assert.Equal("20261113205000", map["vnp_ExpireDate"]);
        Assert.Equal("2.1.0", map["vnp_Version"]);
        Assert.Equal("pay", map["vnp_Command"]);
        Assert.Equal("vnd", map["vnp_Currency"]);
        Assert.Equal("vn", map["vnp_Locale"]);
        Assert.Equal("other", map["vnp_OrderType"]);
        Assert.Equal("Ve%20xem%20phim%20LC123", map["vnp_OrderInfo"]);
        Assert.Equal("127.0.0.1", map["vnp_IpAddr"]);
        Assert.Equal("LC123.AAAA", map["vnp_TxnRef"]);
        Assert.True(Uri.TryCreate(Uri.UnescapeDataString(map["vnp_ReturnUrl"]), UriKind.Absolute, out _));
    }

    [Fact]
    public void Payment_url_signature_matches_an_independent_hmac_over_the_encoded_query()
    {
        var url = CreateService().BuildPaymentUrl(new VnPayPaymentRequest(
            "LC123.AAAA", "Ve xem phim LC123", 120m, CreatedAtUtc, CreatedAtUtc.AddMinutes(5), "127.0.0.1"));

        var pairs = url[(url.IndexOf('?') + 1)..].Split('&').Select(p => p.Split('=', 2)).ToArray();
        var hash = pairs.Single(p => p[0] == "vnp_SecureHash")[1];
        var query = string.Join("&", pairs.Where(p => p[0] != "vnp_SecureHash").Select(p => $"{p[0]}={p[1]}"));

        Assert.Equal(128, hash.Length);
        Assert.Equal(Hmac512(TestOptions.HashSecret, query), hash);
    }

    [Fact]
    public void Signed_callback_validates_and_exposes_the_transaction_fields()
    {
        var data = CreateService().ValidateCallback(SignedCallback());

        Assert.Equal("LC123.AAAA", data.TransactionReference);
        Assert.Equal(12000, data.AmountMinor);
        Assert.Equal("00", data.ResponseCode);
        Assert.Equal("00", data.TransactionStatus);
        Assert.Equal("14103428", data.TransactionId);
        Assert.Equal("20261113204600", data.PayDate);
        Assert.Equal("NCB", data.BankCode);
    }

    [Fact]
    public void Callback_round_trips_through_the_built_payment_url()
    {
        var url = CreateService().BuildPaymentUrl(new VnPayPaymentRequest(
            "LC123.AAAA", "Ve xem phim LC123", 120m, CreatedAtUtc, CreatedAtUtc.AddMinutes(5), "127.0.0.1"));
        var parameters = ParseCallbackQuery(url[(url.IndexOf('?') + 1)..]);
        // The gateway appends its result fields after the merchant signs the request.
        parameters["vnp_ResponseCode"] = ["00"];
        parameters["vnp_TransactionStatus"] = ["00"];
        parameters["vnp_TransactionNo"] = ["14103428"];
        parameters["vnp_PayDate"] = ["20261113204600"];
        parameters["vnp_SecureHash"] = [Hmac512(TestOptions.HashSecret, BuildSignatureQuery(parameters))];

        var data = CreateService().ValidateCallback(parameters);

        Assert.Equal("LC123.AAAA", data.TransactionReference);
        Assert.Equal(12000, data.AmountMinor);
        Assert.Equal("00", data.ResponseCode);
    }

    [Theory]
    [InlineData("vnp_Amount", "999999999999")]
    [InlineData("vnp_OrderInfo", "Ve xem phim khac")]
    [InlineData("vnp_TxnRef", "LC999.BBBB")]
    public void Tampered_callback_values_are_rejected_as_invalid_signature(string key, string tamperedValue)
    {
        var parameters = SignedCallback();
        parameters[key] = [tamperedValue];

        var exception = Assert.Throws<VnPayCallbackException>(() => CreateService().ValidateCallback(parameters));
        Assert.Equal(VnPayResponseCodes.InvalidSignature, exception.RspCode);
    }

    [Fact]
    public void Callback_from_another_merchant_is_rejected()
    {
        var parameters = SignedCallback(fields => fields["vnp_TmnCode"] = "OTHERTMN");

        var exception = Assert.Throws<VnPayCallbackException>(() => CreateService().ValidateCallback(parameters));
        Assert.Equal(VnPayResponseCodes.InvalidSignature, exception.RspCode);
    }

    [Fact]
    public void Repeated_callback_parameters_are_rejected()
    {
        var parameters = SignedCallback();
        parameters["vnp_Amount"] = ["12000", "13000"];

        var exception = Assert.Throws<VnPayCallbackException>(() => CreateService().ValidateCallback(parameters));
        Assert.Equal(VnPayResponseCodes.InvalidSignature, exception.RspCode);
    }

    [Theory]
    [InlineData("vnp_SecureHash")]
    [InlineData("vnp_TxnRef")]
    [InlineData("vnp_PayDate")]
    [InlineData("vnp_ResponseCode")]
    public void Missing_required_fields_are_rejected(string missingKey)
    {
        var parameters = SignedCallback(fields => fields.Remove(missingKey));
        if (missingKey == "vnp_SecureHash")
            parameters.Remove(missingKey);

        var exception = Assert.Throws<VnPayCallbackException>(() => CreateService().ValidateCallback(parameters));
        Assert.Equal(VnPayResponseCodes.InvalidSignature, exception.RspCode);
    }

    [Theory]
    [InlineData("tooshort")]
    [InlineData("zzzz-not-hex-zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void Malformed_hashes_are_rejected(string providedHash)
    {
        var parameters = SignedCallback(hash: providedHash);

        var exception = Assert.Throws<VnPayCallbackException>(() => CreateService().ValidateCallback(parameters));
        Assert.Equal(VnPayResponseCodes.InvalidSignature, exception.RspCode);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1200000")]
    [InlineData("not-a-number")]
    public void Non_positive_or_malformed_amounts_are_rejected(string amount)
    {
        var parameters = SignedCallback(fields => fields["vnp_Amount"] = amount);

        Assert.Throws<VnPayCallbackException>(() => CreateService().ValidateCallback(parameters));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10_000_000_001)]
    public void Out_of_range_payment_amounts_are_rejected_before_signing(decimal amount)
    {
        Assert.Throws<ApiException>(() => CreateService().BuildPaymentUrl(new VnPayPaymentRequest(
            "LC123.AAAA", "Ve xem phim LC123", amount, CreatedAtUtc, CreatedAtUtc.AddMinutes(5), "127.0.0.1")));
    }

    [Fact]
    public void Unconfigured_options_block_url_building_and_validation()
    {
        var service = new VnPayMessageService(Options.Create(new VnPayOptions()));

        var urlException = Assert.Throws<ApiException>(() => service.BuildPaymentUrl(new VnPayPaymentRequest(
            "LC123.AAAA", "Ve xem phim LC123", 120m, CreatedAtUtc, CreatedAtUtc.AddMinutes(5), "127.0.0.1")));
        Assert.Equal(503, urlException.StatusCode);
        Assert.Throws<ApiException>(() => service.ValidateCallback(SignedCallback()));
    }

    private static VnPayMessageService CreateService() => new(Options.Create(TestOptions));

    private static Dictionary<string, string> CallbackFields() => new(StringComparer.Ordinal)
    {
        ["vnp_Amount"] = "12000",
        ["vnp_BankCode"] = "NCB",
        ["vnp_Command"] = "pay",
        ["vnp_CreateDate"] = "20261113204500",
        ["vnp_Currency"] = "vnd",
        ["vnp_ExpireDate"] = "20261113205000",
        ["vnp_IpAddr"] = "127.0.0.1",
        ["vnp_Locale"] = "vn",
        ["vnp_OrderInfo"] = "Ve xem phim LC123",
        ["vnp_OrderType"] = "other",
        ["vnp_PayDate"] = "20261113204600",
        ["vnp_ResponseCode"] = "00",
        ["vnp_TmnCode"] = "LUMITST",
        ["vnp_TransactionNo"] = "14103428",
        ["vnp_TransactionStatus"] = "00",
        ["vnp_TxnRef"] = "LC123.AAAA",
        ["vnp_Version"] = "2.1.0"
    };

    private static Dictionary<string, IReadOnlyList<string>> SignedCallback(
        Action<Dictionary<string, string>>? mutate = null,
        string? hash = null)
    {
        var fields = CallbackFields();
        mutate?.Invoke(fields);
        var parameters = fields.ToDictionary(
            p => p.Key,
            p => (IReadOnlyList<string>)[p.Value]);
        parameters["vnp_SecureHash"] = [hash ?? Hmac512(TestOptions.HashSecret, BuildSignatureQuery(parameters))];
        return parameters;
    }

    private static string BuildSignatureQuery(Dictionary<string, IReadOnlyList<string>> parameters) =>
        string.Join("&", parameters
            .Where(p => p.Key.StartsWith("vnp_", StringComparison.Ordinal) &&
                        p.Key is not ("vnp_SecureHash" or "vnp_SecureHashType"))
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value.Single())}"));

    private static Dictionary<string, IReadOnlyList<string>> ParseCallbackQuery(string query)
    {
        var parameters = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var pair in query.Split('&'))
        {
            var parts = pair.Split('=', 2);
            parameters[parts[0]] = [Uri.UnescapeDataString(parts[1])];
        }
        return parameters;
    }

    private static string Hmac512(string secret, string data)
    {
        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(data))).ToLowerInvariant();
    }
}
