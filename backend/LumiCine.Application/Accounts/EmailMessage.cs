namespace LumiCine.Application.Accounts;

public sealed record EmailMessage(string To, string Subject, string Body);
