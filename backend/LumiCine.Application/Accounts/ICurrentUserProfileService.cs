namespace LumiCine.Application.Accounts;

public interface ICurrentUserProfileService
{
    Task<CurrentUserProfile?> GetAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<CurrentUserProfileUpdateResult> UpdateDisplayNameAsync(Guid userId, string displayName, CancellationToken cancellationToken = default);
}

public sealed record CurrentUserProfile(Guid Id, string Email, string DisplayName, IReadOnlyCollection<string> Roles);

public enum CurrentUserProfileUpdateStatus
{
    Updated,
    NotFound,
    InvalidInput,
    ConcurrencyConflict
}

public sealed record CurrentUserProfileUpdateResult(CurrentUserProfileUpdateStatus Status, CurrentUserProfile? Profile = null);
