namespace LumiCine.Infrastructure.Payments;

public sealed class VnPayOptions
{
    public const string SectionName = "VnPay";

    public string TmnCode { get; set; } = string.Empty;
    public string HashSecret { get; set; } = string.Empty;
    public string PaymentBaseUrl { get; set; } = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html";
    public string ApiBaseUrl { get; set; } = "https://sandbox.vnpayment.vn/merchant_webapi/api/transaction";
    public string ReturnUrl { get; set; } = string.Empty;
    public string IpnUrl { get; set; } = string.Empty;

    public bool IsConfigured() =>
        !string.IsNullOrWhiteSpace(TmnCode) &&
        !string.IsNullOrWhiteSpace(HashSecret) &&
        Uri.TryCreate(PaymentBaseUrl, UriKind.Absolute, out var paymentUrl) && paymentUrl.Scheme == Uri.UriSchemeHttps &&
        Uri.TryCreate(ApiBaseUrl, UriKind.Absolute, out var apiUrl) && apiUrl.Scheme == Uri.UriSchemeHttps &&
        Uri.TryCreate(ReturnUrl, UriKind.Absolute, out var returnUrl) && returnUrl.Scheme == Uri.UriSchemeHttps &&
        Uri.TryCreate(IpnUrl, UriKind.Absolute, out var ipnUrl) && ipnUrl.Scheme == Uri.UriSchemeHttps;
}
