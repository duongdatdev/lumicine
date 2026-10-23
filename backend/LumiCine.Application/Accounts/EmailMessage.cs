namespace LumiCine.Application.Accounts;

public sealed record EmailInlineAttachment(string ContentId, string FileName, string ContentType, byte[] Data);

public sealed record EmailMessage(string To, string Subject, string Body)
{
    public string? HtmlBody { get; init; }
    public IReadOnlyList<EmailInlineAttachment> InlineAttachments { get; init; } = Array.Empty<EmailInlineAttachment>();
}
