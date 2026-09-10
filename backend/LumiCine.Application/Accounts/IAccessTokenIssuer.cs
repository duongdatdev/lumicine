namespace LumiCine.Application.Accounts;

public interface IAccessTokenIssuer
{
    IssuedAccessToken Issue(Guid userId, Guid sessionId, IReadOnlyCollection<string> roles);
}
