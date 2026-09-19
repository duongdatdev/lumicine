namespace LumiCine.Application.Accounts;

public interface IAccountPasswordService
{
    Task<AccountPasswordChangeStatus> ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default);
}

public enum AccountPasswordChangeStatus
{
    Changed,
    InvalidCurrentPassword,
    InvalidNewPassword,
    AccountUnavailable
}
