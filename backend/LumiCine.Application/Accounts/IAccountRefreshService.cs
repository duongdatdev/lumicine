namespace LumiCine.Application.Accounts;

public interface IAccountRefreshService
{
    Task<AccountRefreshResult?> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);
}

public sealed record AccountRefreshResult(
    Guid UserId,
    string Email,
    string DisplayName,
    IReadOnlyCollection<string> Roles,
    string AccessToken,
    int ExpiresInSeconds,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc);
