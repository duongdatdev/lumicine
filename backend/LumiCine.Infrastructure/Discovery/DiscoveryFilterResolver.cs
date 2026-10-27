using LumiCine.Application.Discovery;
using LumiCine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LumiCine.Infrastructure.Discovery;

/// <summary>
/// Validates the parsed intent against real catalog entities: genre slugs must exist and cinema
/// or city mentions must match active cinemas. Ambiguous or unknown locations produce a
/// clarification instead of a guess; the caller decides what to persist.
/// </summary>
public sealed class DiscoveryFilterResolver(LumiCineDbContext db, TimeProvider clock) : IDiscoveryFilterResolver
{
    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    public async Task<DiscoveryFilterResolution> ResolveAsync(string filtersJson, ParsedDiscoveryIntent intent, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var filters = DiscoveryFilters.Parse(filtersJson).Apply(intent, UtcNow, warnings);

        string? clarification = null;
        if (intent.Intent != DiscoveryIntentTypes.Repeat)
        {
            filters = await ResolveGenresAsync(filters, warnings, cancellationToken);
            (filters, clarification) = await ResolveLocationAsync(filters, intent, warnings, cancellationToken);
        }

        return new DiscoveryFilterResolution(intent, filters, warnings, clarification);
    }

    private async Task<DiscoveryFilters> ResolveGenresAsync(DiscoveryFilters filters, List<string> warnings, CancellationToken cancellationToken)
    {
        var requested = filters.GenreSlugs.ToList();
        if (requested.Count == 0)
            return filters;

        var known = (await db.Genres
                .Where(g => requested.Contains(g.Slug))
                .Select(g => g.Slug)
                .ToListAsync(cancellationToken))
            .Select(s => s.ToLowerInvariant())
            .ToHashSet();

        var kept = requested
            .Where(s => known.Contains(s.ToLowerInvariant()))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();
        foreach (var dropped in requested.Where(s => !known.Contains(s.ToLowerInvariant())).Distinct(StringComparer.Ordinal))
            warnings.Add($"Thể loại \"{dropped}\" không có trong danh mục nên đã bỏ qua.");

        return filters with { GenreSlugs = kept };
    }

    private async Task<(DiscoveryFilters Filters, string? Clarification)> ResolveLocationAsync(
        DiscoveryFilters filters, ParsedDiscoveryIntent intent, List<string> warnings, CancellationToken cancellationToken)
    {
        var cinemaName = NonBlank(intent.CinemaName);
        var city = NonBlank(intent.City);
        if (cinemaName is null && city is null)
            return (filters, null);

        var cinemas = (await db.Cinemas
                .Where(c => c.IsActive)
                .Select(c => new { c.Id, c.Name, c.City })
                .ToListAsync(cancellationToken))
            // Deterministic order keeps clarification listings stable across turns.
            .OrderBy(c => c.Name, StringComparer.Ordinal)
            .ThenBy(c => c.City, StringComparer.Ordinal)
            .ToList();
        var cities = cinemas.Select(c => c.City).Distinct(StringComparer.Ordinal).OrderBy(c => c, StringComparer.Ordinal).ToList();

        string? clarification = null;
        if (cinemaName is not null)
        {
            var matches = cinemas
                .Where(c => string.Equals(c.Name.Trim(), cinemaName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matches.Count == 1)
            {
                filters = filters with { CinemaId = matches[0].Id, CinemaName = matches[0].Name };
            }
            else if (matches.Count > 1)
            {
                var listed = matches.Select(c => $"{c.Name} ({c.City})").Distinct(StringComparer.Ordinal);
                clarification = $"Rạp \"{cinemaName}\" khớp với nhiều rạp: {string.Join("; ", listed)}. Bạn muốn xem ở rạp nào?";
            }
            else
            {
                var cityMatch = cities.FirstOrDefault(c => string.Equals(c.Trim(), cinemaName, StringComparison.OrdinalIgnoreCase));
                if (cityMatch is not null)
                {
                    // The user named a city instead of a cinema; reinterpret without complaining.
                    filters = filters with { City = cityMatch, CinemaId = null, CinemaName = null };
                }
                else
                {
                    var known = cinemas.Select(c => c.Name).Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal).Take(6).ToList();
                    clarification = $"Mình chưa tìm thấy rạp nào khớp \"{cinemaName}\". Các rạp đang có: {string.Join("; ", known)}.";
                }
            }
        }
        else if (city is not null)
        {
            var matches = cities.Where(c => string.Equals(c.Trim(), city, StringComparison.OrdinalIgnoreCase))
                .OrderBy(c => c, StringComparer.Ordinal)
                .ToList();
            if (matches.Count == 1)
            {
                filters = filters with { City = matches[0] };
            }
            else if (matches.Count == 0)
            {
                filters = filters with { City = null };
                warnings.Add($"Mình chưa tìm thấy thành phố nào khớp \"{city}\" nên đã bỏ qua điều kiện thành phố.");
            }
            else
            {
                filters = filters with { City = matches[0] };
            }
        }

        return (filters, clarification);
    }

    private static string? NonBlank(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
