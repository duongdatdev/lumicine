namespace LumiCine.Application.Accounts;

public sealed record AdminUserSummary(
    Guid Id,
    string Email,
    string DisplayName,
    bool IsActive,
    bool EmailConfirmed,
    IReadOnlyList<string> Roles,
    DateTime CreatedAtUtc);
