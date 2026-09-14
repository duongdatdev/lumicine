using System.Text;
using LumiCine.Application.Accounts;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace LumiCine.Infrastructure.Identity;

internal sealed class AccountEmailService(
    UserManager<ApplicationUser> userManager,
    IEmailSender emailSender,
    IOptions<AccountLinksOptions> linksOptions) : IAccountEmailService
{
    private readonly Uri _baseUri = new(linksOptions.Value.BaseUrl, UriKind.Absolute);

    public async Task SendConfirmationAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await userManager.FindByIdAsync(userId.ToString("D"));
        if (user is null || !user.IsActive || user.EmailConfirmed || string.IsNullOrWhiteSpace(user.Email))
        {
            return;
        }

        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        var code = EncodeToken(token);
        var link = BuildLink("/account/confirm-email", $"userId={Uri.EscapeDataString(user.Id.ToString("D"))}&code={Uri.EscapeDataString(code)}");
        await emailSender.SendAsync(new EmailMessage(
            user.Email,
            "Confirm your LumiCine email",
            $"Confirm your email by opening this link:\n{link}"), cancellationToken);
    }

    public async Task SendPasswordResetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await userManager.FindByIdAsync(userId.ToString("D"));
        if (user is null || !user.IsActive || !user.EmailConfirmed || string.IsNullOrWhiteSpace(user.Email))
        {
            return;
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var code = EncodeToken(token);
        var query = $"email={Uri.EscapeDataString(user.Email)}&code={Uri.EscapeDataString(code)}";
        var link = BuildLink("/account/reset-password", query);
        await emailSender.SendAsync(new EmailMessage(
            user.Email,
            "Reset your LumiCine password",
            $"Reset your password by opening this link:\n{link}"), cancellationToken);
    }

    private Uri BuildLink(string path, string query)
    {
        var builder = new UriBuilder(_baseUri) { Path = path, Query = query };
        return builder.Uri;
    }

    private static string EncodeToken(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
}
