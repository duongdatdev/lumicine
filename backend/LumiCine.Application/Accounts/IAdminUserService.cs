namespace LumiCine.Application.Accounts;

public interface IAdminUserService
{
    Task<AdminUserPage> GetUsersAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task<AdminUserSummary?> GetUserAsync(Guid id, CancellationToken cancellationToken = default);
}
