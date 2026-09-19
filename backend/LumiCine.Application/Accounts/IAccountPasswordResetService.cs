namespace LumiCine.Application.Accounts;

public interface IAccountPasswordResetService
{
    Task<bool> ResetAsync(string email, string encodedCode, string newPassword, CancellationToken cancellationToken = default);
}
