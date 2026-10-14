using LumiCine.Infrastructure.Payments;

namespace LumiCine.IntegrationTests.Payments;

public sealed class VnPayOptionsTests
{
    private static VnPayOptions ValidOptions() => new()
    {
        TmnCode = "LUMITST",
        HashSecret = "test-secret-value-0123456789",
        ReturnUrl = "https://lumicine.example.test/payments/vnpay/return",
        IpnUrl = "https://lumicine.example.test/payments/vnpay/ipn"
    };

    [Fact]
    public void Sandbox_defaults_point_at_vnpay_sandbox_endpoints()
    {
        var options = new VnPayOptions();

        Assert.Equal("https://sandbox.vnpayment.vn/paymentv2/vpcpay.html", options.PaymentBaseUrl);
        Assert.Equal("https://sandbox.vnpayment.vn/merchant_webapi/api/transaction", options.ApiBaseUrl);
    }

    [Fact]
    public void Configured_options_are_accepted()
    {
        Assert.True(ValidOptions().IsConfigured());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-url")]
    [InlineData("http://insecure.example.test/callback")]
    public void Insecure_or_missing_callback_urls_are_rejected(string returnUrl)
    {
        var options = ValidOptions();
        options.ReturnUrl = returnUrl;

        Assert.False(options.IsConfigured());
    }

    [Fact]
    public void Missing_merchant_or_secret_is_rejected()
    {
        var withoutTmn = ValidOptions();
        withoutTmn.TmnCode = " ";
        Assert.False(withoutTmn.IsConfigured());

        var withoutSecret = ValidOptions();
        withoutSecret.HashSecret = "";
        Assert.False(withoutSecret.IsConfigured());
    }
}
