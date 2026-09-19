namespace LumiCine.Application.Accounts;

public sealed record AdminUserPage(
    IReadOnlyList<AdminUserSummary> Items,
    int TotalCount,
    int Page,
    int PageSize);
