namespace LumiCine.Application.Accounts;

public interface IAccountRegistrationService
{
    Task<AccountRegistrationResult> RegisterAsync(
        string email,
        string displayName,
        string password,
        CancellationToken cancellationToken = default);
}

public enum AccountRegistrationStatus
{
    Created,
    DuplicateEmail,
    InvalidInput
}

public sealed record AccountRegistrationResult(
    AccountRegistrationStatus Status,
    Guid? UserId = null,
    string? Email = null,
    string? DisplayName = null);
