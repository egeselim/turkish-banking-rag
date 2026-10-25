namespace Assistant.Rag.Search;

/// <summary>
/// Reciprocal Rank Fusion: score(d) = Σ 1 / (k + rank_i(d)) over the ranked lists that contain d.
/// Uses ranks only, so cosine similarities and BM25 scores need no calibration against each other.
/// </summary>
public static class ReciprocalRankFusion
{
    public const int DefaultK = 60;

    public static IReadOnlyList<(Guid Id, double Score)> Fuse(IEnumerable<IReadOnlyList<Guid>> rankedLists, int k = DefaultK)
    {
        ArgumentNullException.ThrowIfNull(rankedLists);

        var scores = new Dictionary<Guid, double>();
        var firstSeen = new Dictionary<Guid, int>();
        foreach (var list in rankedLists)
        {
            for (int rank = 0; rank < list.Count; rank++)
            {
                scores[list[rank]] = scores.GetValueOrDefault(list[rank]) + (1.0 / (k + rank + 1));
                firstSeen.TryAdd(list[rank], firstSeen.Count);
            }
        }

        // Ties keep a stable order: the one seen first (earlier list, higher rank) wins.
        return [.. scores.OrderByDescending(s => s.Value).ThenBy(s => firstSeen[s.Key]).Select(s => (s.Key, s.Value))];
    }
}
