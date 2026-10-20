namespace LumiCine.Application.Payments;

public sealed record VnPayTransactionQueryResult(
    string ResponseCode,
    string Message,
    string? TransactionNo,
    string? TransactionStatus,
    long? AmountMinor);

public interface IVnPayTransactionQueryClient
{
    Task<VnPayTransactionQueryResult> QueryAsync(string transactionReference, decimal amount, DateTime transactionDateUtc, CancellationToken cancellationToken);
}
