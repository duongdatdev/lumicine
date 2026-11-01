using System.ComponentModel.DataAnnotations;

namespace LumiCine.API.Authentication;

public sealed class DiscoveryRateLimitOptions
{
    public const string SectionName = "Ai:DiscoveryRateLimit";

    [Range(1, 10_000)]
    public int PermitLimit { get; init; } = 10;

    [Range(1, 3600)]
    public int WindowSeconds { get; init; } = 60;
}
