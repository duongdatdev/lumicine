using System.Text;
using LumiCine.Application.Accounts;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;

namespace LumiCine.Infrastructure.Identity;

internal sealed class EmailConfirmationService(UserManager<ApplicationUser> userManager) : IEmailConfirmationService
{
    public async Task<EmailConfirmationResult> ConfirmAsync(
        Guid userId,
        string encodedCode,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (userId == Guid.Empty)
        {
            return EmailConfirmationResult.InvalidToken;
        }

        string token;
        try
        {
            token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encodedCode));
        }
        catch (FormatException)
        {
            return EmailConfirmationResult.InvalidToken;
        }

        var user = await userManager.FindByIdAsync(userId.ToString("D"));
        if (user is null || !user.IsActive || string.IsNullOrWhiteSpace(user.Email))
        {
            return EmailConfirmationResult.InvalidToken;
        }

        var result = await userManager.ConfirmEmailAsync(user, token);
        return result.Succeeded ? EmailConfirmationResult.Confirmed : EmailConfirmationResult.InvalidToken;
    }
}
