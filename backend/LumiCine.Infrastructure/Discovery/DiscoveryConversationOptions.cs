using System.ComponentModel.DataAnnotations;

namespace LumiCine.Infrastructure.Discovery;

/// <summary>
/// Technical limits for the discovery chat; defaults follow the phase-6 baseline
/// (20 turns, 7-day inactivity, 5 movies, 3 showtimes, 6 context turns).
/// </summary>
public sealed class DiscoveryConversationOptions
{
    public const string SectionName = "Ai:Discovery";

    [Range(1, 100)]
    public int MaxTurns { get; init; } = 20;

    [Range(1, 1000)]
    public int MessageMaxLength { get; init; } = 1000;

    [Range(1, 365)]
    public int InactivityDays { get; init; } = 7;

    [Range(1, 50)]
    public int ResultMovieLimit { get; init; } = 5;

    [Range(1, 10)]
    public int ShowtimesPerMovieLimit { get; init; } = 3;

    [Range(1, 20)]
    public int ContextTurnLimit { get; init; } = 6;
}
