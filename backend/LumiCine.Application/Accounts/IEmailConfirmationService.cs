namespace LumiCine.Application.Accounts;

public interface IEmailConfirmationService
{
    Task<EmailConfirmationResult> ConfirmAsync(Guid userId, string encodedCode, CancellationToken cancellationToken = default);
}

public enum EmailConfirmationResult
{
    Confirmed,
    InvalidToken
}
