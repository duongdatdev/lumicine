using LumiCine.Application.Accounts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using LumiCine.Infrastructure.Persistence;
using Npgsql;

namespace LumiCine.Infrastructure.Identity;

internal sealed class AccountRegistrationService(
    UserManager<ApplicationUser> userManager,
    LumiCineDbContext dbContext,
    TimeProvider timeProvider) : IAccountRegistrationService
{
    public async Task<AccountRegistrationResult> RegisterAsync(
        string email,
        string displayName,
        string password,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        email = email.Trim();
        displayName = displayName.Trim();
        if (email.Length is 0 or > 254 || displayName.Length is 0 or > 100)
        {
            return new AccountRegistrationResult(AccountRegistrationStatus.InvalidInput);
        }

        try
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = email,
                Email = email,
                DisplayName = displayName,
                IsActive = true,
                CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime
            };
            var createResult = await userManager.CreateAsync(user, password);
            if (!createResult.Succeeded)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new AccountRegistrationResult(
                    IsDuplicate(createResult) ? AccountRegistrationStatus.DuplicateEmail : AccountRegistrationStatus.InvalidInput);
            }

            var roleResult = await userManager.AddToRoleAsync(user, RoleNames.Customer);
            if (!roleResult.Succeeded)
            {
                throw new InvalidOperationException("New account could not be assigned the customer role.");
            }

            await transaction.CommitAsync(cancellationToken);
            return new AccountRegistrationResult(AccountRegistrationStatus.Created, user.Id, user.Email, user.DisplayName);
        }
        catch (DbUpdateException exception) when (IsEmailUniqueViolation(exception))
        {
            return new AccountRegistrationResult(AccountRegistrationStatus.DuplicateEmail);
        }
    }

    private static bool IsDuplicate(IdentityResult result) =>
        result.Errors.Any(error => error.Code is "DuplicateEmail" or "DuplicateUserName");

    private static bool IsEmailUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        } postgres &&
        (postgres.ConstraintName == "EmailIndex" || postgres.ConstraintName == "UserNameIndex");
}
