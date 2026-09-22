using System.ComponentModel.DataAnnotations;

namespace LumiCine.API.Authentication;

public sealed class AuthRateLimitOptions
{
    public const string SectionName = "AuthRateLimit";

    [Range(1, 1_000_000)]
    public int PermitLimit { get; set; } = 20;

    [Range(1, 3600)]
    public int WindowSeconds { get; set; } = 60;
}
