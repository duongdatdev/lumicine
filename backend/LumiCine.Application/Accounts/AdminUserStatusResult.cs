namespace LumiCine.Application.Accounts;

public enum AdminUserStatusResult
{
    Success,
    NotFound,
    ProtectedAdmin,
    Conflict,
    InvalidRole
}
