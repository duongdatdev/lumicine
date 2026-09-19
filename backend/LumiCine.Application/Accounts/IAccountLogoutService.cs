namespace LumiCine.Application.Accounts;

public interface IAccountLogoutService
{
    Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken = default);
}
