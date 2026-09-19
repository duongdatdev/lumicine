using LumiCine.Application.Accounts;
using LumiCine.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LumiCine.Infrastructure.Identity;

internal sealed class AdminUserService(
    UserManager<ApplicationUser> userManager,
    LumiCineDbContext dbContext) : IAdminUserService
{
    public async Task<AdminUserPage> GetUsersAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var totalCount = await dbContext.Users.AsNoTracking().CountAsync(cancellationToken);
        var offset = ((long)page - 1) * pageSize;
        if (offset >= totalCount)
        {
            return new AdminUserPage([], totalCount, page, pageSize);
        }

        var users = await dbContext.Users.AsNoTracking()
            .OrderByDescending(user => user.CreatedAtUtc)
            .ThenBy(user => user.Id)
            .Skip((int)offset)
            .Take(pageSize)
            .Select(user => new
            {
                user.Id,
                user.Email,
                user.DisplayName,
                user.IsActive,
                user.EmailConfirmed,
                user.CreatedAtUtc
            })
            .ToArrayAsync(cancellationToken);

        var userIds = users.Select(user => user.Id).ToArray();
        var userRoles = await (
            from userRole in dbContext.UserRoles.AsNoTracking()
            join role in dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            where userIds.Contains(userRole.UserId)
            select new { userRole.UserId, role.Name })
            .ToArrayAsync(cancellationToken);
        var rolesByUser = userRoles
            .GroupBy(userRole => userRole.UserId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group
                    .Select(userRole => userRole.Name!)
                    .Order(StringComparer.Ordinal)
                    .ToArray());

        var items = users.Select(user => new AdminUserSummary(
                user.Id,
                user.Email!,
                user.DisplayName,
                user.IsActive,
                user.EmailConfirmed,
                rolesByUser.GetValueOrDefault(user.Id, []),
                user.CreatedAtUtc))
            .ToArray();
        return new AdminUserPage(items, totalCount, page, pageSize);
    }

    public async Task<AdminUserSummary?> GetUserAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await userManager.FindByIdAsync(id.ToString("D"));
        if (user is null)
        {
            return null;
        }

        var roles = await userManager.GetRolesAsync(user);
        cancellationToken.ThrowIfCancellationRequested();
        return new AdminUserSummary(
            user.Id,
            user.Email!,
            user.DisplayName,
            user.IsActive,
            user.EmailConfirmed,
            roles.Order(StringComparer.Ordinal).ToArray(),
            user.CreatedAtUtc);
    }
}
