namespace LumiCine.Infrastructure.Discovery;

/// <summary>
/// Lexical relevance for discovery candidates: each query term scores title, original title,
/// genre and synopsis matches so keyword search works without the AI embedding service.
/// </summary>
public static class DiscoveryKeywordScorer
{
    public static double Score(string? queryText, string title, string? originalTitle, IReadOnlyList<string> genreNames, string? synopsis)
    {
        if (string.IsNullOrWhiteSpace(queryText))
            return 0;
        var terms = Tokenize(queryText);
        if (terms.Count == 0)
            return 0;
        title ??= string.Empty;
        var original = originalTitle ?? string.Empty;
        var plot = synopsis ?? string.Empty;
        var genres = genreNames ?? [];
        double score = 0;
        foreach (var term in terms)
        {
            if (title.Contains(term, StringComparison.OrdinalIgnoreCase))
                score += 3;
            if (original.Contains(term, StringComparison.OrdinalIgnoreCase))
                score += 2;
            if (genres.Any(genre => genre.Contains(term, StringComparison.OrdinalIgnoreCase)))
                score += 2;
            if (plot.Contains(term, StringComparison.OrdinalIgnoreCase))
                score += 1;
        }
        return score;
    }

    /// <summary>Splits on non-alphanumerics, keeps Vietnamese diacritics, drops 1-char terms, deduplicates.</summary>
    private static List<string> Tokenize(string text)
    {
        var terms = new List<string>();
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && char.IsLetterOrDigit(text[i]))
            {
                if (start < 0)
                    start = i;
            }
            else if (start >= 0)
            {
                if (i - start >= 2)
                    terms.Add(text[start..i].ToLowerInvariant());
                start = -1;
            }
        }
        return terms.Distinct().ToList();
    }
}
