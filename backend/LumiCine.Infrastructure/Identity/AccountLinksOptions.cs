namespace LumiCine.Infrastructure.Identity;

public sealed class AccountLinksOptions
{
    public const string SectionName = "AccountLinks";
    public string BaseUrl { get; set; } = "https://localhost:7005";

    internal bool IsValid() =>
        Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        uri.AbsolutePath == "/" &&
        string.IsNullOrEmpty(uri.Query) &&
        string.IsNullOrEmpty(uri.Fragment) &&
        string.IsNullOrEmpty(uri.UserInfo);
}
