using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LumiCine.Application.Common;
using LumiCine.Application.Payments;
using LumiCine.Infrastructure.Payments;
using Microsoft.Extensions.Options;

namespace LumiCine.IntegrationTests.Payments;

public sealed class VnPayTransactionQueryClientTests
{
    private static readonly VnPayOptions Options = new()
    {
        TmnCode = "LUMITEST",
        HashSecret = "test-secret-value-0123456789",
        ReturnUrl = "https://lumicine.test/payments/vnpay/return",
        IpnUrl = "https://lumicine.test/payments/vnpay/ipn"
    };

    [Fact]
    public async Task Query_signs_the_request_and_parses_a_signed_response()
    {
        var handler = new FakeVnPayApiHandler(Options.HashSecret, responseCode: "00", transactionStatus: "00");
        var client = new VnPayTransactionQueryClient(
            new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(Options), TimeProvider.System);
        var transactionDate = new DateTime(2026, 11, 13, 13, 45, 0, DateTimeKind.Utc);

        var result = await client.QueryAsync("LC123.AAAA", 200, transactionDate, CancellationToken.None);

        Assert.Equal("00", result.ResponseCode);
        Assert.Equal("00", result.TransactionStatus);
        Assert.Equal("778899", result.TransactionNo);
        Assert.Equal(20000, result.AmountMinor);
        Assert.Contains("LC123.AAAA", handler.ReceivedChecksumInput);
        Assert.Contains("querydr", handler.ReceivedChecksumInput);
    }

    [Fact]
    public async Task Tampered_query_responses_are_rejected()
    {
        var handler = new FakeVnPayApiHandler(Options.HashSecret, "00", "00", tamperResponse: true);
        var client = new VnPayTransactionQueryClient(
            new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(Options), TimeProvider.System);

        var exception = await Assert.ThrowsAsync<VnPayCallbackException>(() =>
            client.QueryAsync("LC123.AAAA", 200, DateTime.UtcNow, CancellationToken.None));

        Assert.Equal(VnPayResponseCodes.UnknownError, exception.RspCode);
    }

    [Fact]
    public async Task Query_requires_configuration()
    {
        var client = new VnPayTransactionQueryClient(
            new HttpClient(new FakeVnPayApiHandler(Options.HashSecret, "00", "00")), Microsoft.Extensions.Options.Options.Create(new VnPayOptions()), TimeProvider.System);

        var exception = await Assert.ThrowsAsync<ApiException>(() =>
            client.QueryAsync("LC123.AAAA", 200, DateTime.UtcNow, CancellationToken.None));

        Assert.Equal(503, exception.StatusCode);
    }

    private sealed class FakeVnPayApiHandler(string hashSecret, string responseCode, string transactionStatus, bool tamperResponse = false) : HttpMessageHandler
    {
        public string? ReceivedChecksumInput { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var reader = new StreamReader(request.Content!.ReadAsStream(cancellationToken));
            var fields = JsonDocument.Parse(await reader.ReadToEndAsync(cancellationToken)).RootElement.EnumerateObject()
                .ToDictionary(property => property.Name, property => property.Value.ToString(), StringComparer.Ordinal);
            var requestId = fields["vnp_RequestId"];
            var checksumInput = string.Join("|", requestId, "2.1.0", "querydr", fields["vnp_TmnCode"],
                fields["vnp_TxnRef"], fields["vnp_OrderInfo"], fields["vnp_TransactionDate"], fields["vnp_CreateDate"], fields["vnp_IpAddr"]);
            using (var verify = new HMACSHA512(Encoding.UTF8.GetBytes(hashSecret)))
            {
                var expected = Convert.ToHexString(verify.ComputeHash(Encoding.UTF8.GetBytes(checksumInput))).ToLowerInvariant();
                if (!string.Equals(expected, fields["vnp_SecureHash"], StringComparison.Ordinal))
                    return new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest);
            }
            ReceivedChecksumInput = checksumInput;
            var responseFields = new Dictionary<string, string>
            {
                ["vnp_ResponseId"] = requestId,
                ["vnp_Command"] = "querydr",
                ["vnp_TmnCode"] = "LUMITEST",
                ["vnp_TxnRef"] = fields["vnp_TxnRef"],
                ["vnp_Amount"] = "20000",
                ["vnp_BankCode"] = "NCB",
                ["vnp_PayDate"] = "20261113204600",
                ["vnp_TransactionNo"] = "778899",
                ["vnp_ResponseCode"] = responseCode,
                ["vnp_Message"] = "Confirm Success",
                ["vnp_TransactionStatus"] = transactionStatus
            };
            var responseChecksumInput = string.Join("|", requestId, "querydr", "LUMITEST", fields["vnp_TxnRef"], "20000", "NCB",
                "20261113204600", "778899", responseCode, "Confirm Success", transactionStatus);
            using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(hashSecret));
            var hash = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(responseChecksumInput))).ToLowerInvariant();
            if (tamperResponse)
            {
                responseFields["vnp_ResponseCode"] = "11";
            }
            responseFields["vnp_SecureHash"] = hash;
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(responseFields), Encoding.UTF8, "application/json")
            };
        }
    }
}
