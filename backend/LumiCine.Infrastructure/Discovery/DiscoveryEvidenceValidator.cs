using System.Text;
using System.Text.RegularExpressions;
using LumiCine.Application.Discovery;

namespace LumiCine.Infrastructure.Discovery;

/// <summary>Result of validating an AI answer: the expanded text when valid, otherwise empty.</summary>
public sealed record DiscoveryEvidenceValidation(bool IsValid, string Text);

/// <summary>
/// Expands the answer writer's evidence tokens into server-computed values (times, prices,
/// durations). Answers referencing ids outside the evidence or using unknown token forms are
/// rejected, so nothing ungrounded ever reaches the user.
/// </summary>
public static class DiscoveryEvidenceValidator
{
    private static readonly Regex TokenPattern =
        new(@"\[(movie|showtime):([0-9a-fA-F-]{36})(?::(thoiluong|gio|gia))?\]",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // A leftover movie/showtime bracket means a malformed token the strict pattern did not accept.
    private static readonly Regex LeftoverTokenPattern =
        new(@"\[(movie|showtime):", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static DiscoveryEvidenceValidation Validate(string? answer, IReadOnlyList<DiscoveryEvidenceMovie> evidence)
    {
        if (string.IsNullOrWhiteSpace(answer))
            return new(false, string.Empty);

        var movies = evidence.ToDictionary(m => m.MovieId);
        var showtimes = evidence
            .SelectMany(m => m.Showtimes.Select(s => (Movie: m, Showtime: s)))
            .ToDictionary(pair => pair.Showtime.ShowtimeId);

        var sb = new StringBuilder(answer.Length);
        var last = 0;
        foreach (Match match in TokenPattern.Matches(answer))
        {
            sb.Append(answer, last, match.Index - last);
            var kind = match.Groups[1].Value;
            var suffix = match.Groups[3].Value;
            var idParsed = Guid.TryParse(match.Groups[2].Value, out var id);

            if (kind == "movie" && idParsed && movies.TryGetValue(id, out var movie))
            {
                if (suffix.Length == 0)
                    sb.Append(movie.Title);
                else if (suffix == "thoiluong")
                    sb.Append($"{movie.DurationMinutes} phút");
                else
                    return new(false, string.Empty);
            }
            else if (kind == "showtime" && idParsed && showtimes.TryGetValue(id, out var pair))
            {
                if (suffix.Length == 0 || suffix == "gio")
                {
                    var start = pair.Showtime.StartAtUtc + DiscoveryTime.ZoneOffset;
                    sb.Append($"{start:HH:mm} ngày {start:dd/MM}");
                }
                else if (suffix == "gia")
                    sb.Append(DiscoveryAnswerTemplates.FormatVnd(pair.Showtime.Price));
                else
                    return new(false, string.Empty);
            }
            else
            {
                // Unknown id, id across kinds, or a reference the lookup cannot ground.
                return new(false, string.Empty);
            }
            last = match.Index + match.Length;
        }

        sb.Append(answer, last, answer.Length - last);
        var text = sb.ToString();
        if (LeftoverTokenPattern.IsMatch(text))
            return new(false, string.Empty);
        return new(true, text);
    }
}
