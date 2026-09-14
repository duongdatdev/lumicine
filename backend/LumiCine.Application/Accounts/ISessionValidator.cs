namespace LumiCine.Application.Accounts;

public interface ISessionValidator
{
    Task<bool> IsValidAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default);
}
