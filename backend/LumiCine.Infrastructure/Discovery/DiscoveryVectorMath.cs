namespace LumiCine.Infrastructure.Discovery;

/// <summary>
/// Pure ranking math for discovery: cosine similarity over embedding vectors and Reciprocal
/// Rank Fusion across independent rankings. No I/O so both operations are unit-testable.
/// </summary>
public static class DiscoveryVectorMath
{
    /// <summary>Cosine similarity with double accumulation; malformed input yields 0.</summary>
    public static double CosineSimilarity(float[]? a, float[]? b)
    {
        if (a is null || b is null || a.Length != b.Length)
            return 0;
        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += (double)a[i] * b[i];
            normA += (double)a[i] * a[i];
            normB += (double)b[i] * b[i];
        }
        if (normA == 0 || normB == 0)
            return 0;
        return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }

    /// <summary>
    /// Fuses rankings with Reciprocal Rank Fusion: each list contributes 1/(constant + rank)
    /// per id (rank is 1-based). Result is ordered by score descending; ties keep first
    /// appearance. Ids repeated inside one list count once, at their first (best) rank.
    /// </summary>
    public static IReadOnlyList<(Guid Id, double Score)> ReciprocalRankFusion(
        IReadOnlyList<IReadOnlyList<Guid>?> rankings, int constant = 60)
    {
        if (rankings is null)
            return [];
        var scores = new Dictionary<Guid, double>();
        var firstSeen = new Dictionary<Guid, int>();
        var appearances = 0;
        foreach (var ranking in rankings)
        {
            if (ranking is null)
                continue;
            var seen = new HashSet<Guid>();
            var rank = 0;
            foreach (var id in ranking)
            {
                if (!seen.Add(id))
                    continue;
                rank++;
                scores[id] = scores.GetValueOrDefault(id) + 1.0 / (constant + rank);
                if (!firstSeen.ContainsKey(id))
                    firstSeen[id] = appearances++;
            }
        }
        return scores
            .OrderByDescending(x => x.Value)
            .ThenBy(x => firstSeen[x.Key])
            .Select(x => (x.Key, x.Value))
            .ToList();
    }
}
