using LumiCine.Application.Accounts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LumiCine.Infrastructure.Identity;

internal sealed class CurrentUserProfileService(UserManager<ApplicationUser> userManager) : ICurrentUserProfileService
{
    public async Task<CurrentUserProfile?> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await userManager.Users.SingleOrDefaultAsync(item => item.Id == userId, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var roles = await userManager.GetRolesAsync(user);
        return new CurrentUserProfile(user.Id, user.Email!, user.DisplayName, roles.ToArray());
    }

    public async Task<CurrentUserProfileUpdateResult> UpdateDisplayNameAsync(Guid userId, string displayName, CancellationToken cancellationToken = default)
    {
        displayName = displayName.Trim();
        if (displayName.Length is 0 or > 100)
        {
            return new CurrentUserProfileUpdateResult(CurrentUserProfileUpdateStatus.InvalidInput);
        }

        var user = await userManager.Users.SingleOrDefaultAsync(item => item.Id == userId, cancellationToken);
        if (user is null)
        {
            return new CurrentUserProfileUpdateResult(CurrentUserProfileUpdateStatus.NotFound);
        }

        user.DisplayName = displayName;
        var update = await userManager.UpdateAsync(user);
        if (!update.Succeeded)
        {
            if (update.Errors.Any(error => error.Code == "ConcurrencyFailure"))
            {
                return new CurrentUserProfileUpdateResult(CurrentUserProfileUpdateStatus.ConcurrencyConflict);
            }

            throw new InvalidOperationException("Profile update failed.");
        }

        var roles = await userManager.GetRolesAsync(user);
        return new CurrentUserProfileUpdateResult(
            CurrentUserProfileUpdateStatus.Updated,
            new CurrentUserProfile(user.Id, user.Email!, user.DisplayName, roles.ToArray()));
    }
}
