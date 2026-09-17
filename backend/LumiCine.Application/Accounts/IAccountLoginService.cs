namespace LumiCine.Application.Accounts;

public interface IAccountLoginService
{
    Task<AccountLoginResult?> LoginAsync(string email, string password, CancellationToken cancellationToken = default);
}

public sealed record AccountLoginResult(
    Guid UserId,
    string Email,
    string DisplayName,
    IReadOnlyCollection<string> Roles,
    string AccessToken,
    int ExpiresInSeconds,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc);
