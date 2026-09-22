using LumiCine.Application.Accounts;
using LumiCine.Infrastructure.Persistence;
using LumiCine.Infrastructure.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LumiCine.Infrastructure.Identity;

internal sealed class AdminUserService(
    UserManager<ApplicationUser> userManager,
    LumiCineDbContext dbContext,
    TimeProvider timeProvider) : IAdminUserService
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

    public async Task<AdminUserStatusResult> SetActiveAsync(
        Guid id,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var user = await userManager.FindByIdAsync(id.ToString("D"));
            cancellationToken.ThrowIfCancellationRequested();
            if (user is null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return AdminUserStatusResult.NotFound;
            }

            var isAdmin = await userManager.IsInRoleAsync(user, RoleNames.Admin);
            cancellationToken.ThrowIfCancellationRequested();
            if (isAdmin)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return AdminUserStatusResult.ProtectedAdmin;
            }

            var needsIdentityUpdate = user.IsActive != isActive;
            user.IsActive = isActive;
            if (isActive)
            {
                needsIdentityUpdate |= user.LockoutEnd is not null || user.AccessFailedCount != 0;
                user.LockoutEnd = null;
                user.AccessFailedCount = 0;
            }

            if (needsIdentityUpdate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var updateResult = await userManager.UpdateAsync(user);
                cancellationToken.ThrowIfCancellationRequested();
                if (!updateResult.Succeeded)
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    return AdminUserStatusResult.Conflict;
                }
            }

            if (!isActive)
            {
                var revokedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
                await dbContext.AuthSessions
                    .Where(session => session.UserId == user.Id && session.RevokedAtUtc == null)
                    .ExecuteUpdateAsync(update => update.SetProperty(session => session.RevokedAtUtc, revokedAtUtc), cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return AdminUserStatusResult.Success;
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return AdminUserStatusResult.Conflict;
        }
    }

    public async Task<AdminUserStatusResult> SetRoleAsync(
        Guid id,
        string role,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (role is not RoleNames.Customer and not RoleNames.Staff)
        {
            return AdminUserStatusResult.InvalidRole;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var user = await dbContext.Users
                .FromSqlInterpolated($"SELECT * FROM \"Users\" WHERE \"Id\" = {id} FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (user is null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return AdminUserStatusResult.NotFound;
            }

            var isAdmin = await userManager.IsInRoleAsync(user, RoleNames.Admin);
            cancellationToken.ThrowIfCancellationRequested();
            if (isAdmin)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return AdminUserStatusResult.ProtectedAdmin;
            }

            var currentRoles = await userManager.GetRolesAsync(user);
            cancellationToken.ThrowIfCancellationRequested();
            if (currentRoles.Count == 1 && string.Equals(currentRoles[0], role, StringComparison.Ordinal))
            {
                await transaction.CommitAsync(cancellationToken);
                return AdminUserStatusResult.Success;
            }

            if (currentRoles.Count > 0)
            {
                var removeResult = await userManager.RemoveFromRolesAsync(user, currentRoles);
                cancellationToken.ThrowIfCancellationRequested();
                if (!removeResult.Succeeded)
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    return AdminUserStatusResult.Conflict;
                }
            }

            var addResult = await userManager.AddToRoleAsync(user, role);
            cancellationToken.ThrowIfCancellationRequested();
            if (!addResult.Succeeded)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return AdminUserStatusResult.Conflict;
            }

            var revokedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            await dbContext.AuthSessions
                .Where(session => session.UserId == user.Id && session.RevokedAtUtc == null)
                .ExecuteUpdateAsync(update => update.SetProperty(session => session.RevokedAtUtc, revokedAtUtc), cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return AdminUserStatusResult.Success;
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return AdminUserStatusResult.Conflict;
        }
    }
}
