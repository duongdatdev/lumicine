namespace LumiCine.Application.Accounts;

public interface IAccountEmailService
{
    Task SendConfirmationAsync(Guid userId, CancellationToken cancellationToken = default);
    Task SendPasswordResetAsync(Guid userId, CancellationToken cancellationToken = default);
}
