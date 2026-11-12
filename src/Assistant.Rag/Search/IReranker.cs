namespace Assistant.Rag.Search;

/// <summary>Scores how well each passage answers the query; higher is better. One score per passage, in input order.</summary>
public interface IReranker
{
    Task<IReadOnlyList<double>> ScoreAsync(string query, IReadOnlyList<string> passages, CancellationToken cancellationToken = default);
}

/// <summary>
/// Re-ranking on top of another retriever: takes <see cref="Candidates"/> results from it with the same mode,
/// orders them by the reranker's score and returns the top ones (step 3.4, ADR-003). Ties keep the inner order.
/// </summary>
public sealed class RerankingRetriever(IDocumentRetriever inner, IReranker reranker, int candidates = RerankingRetriever.DefaultCandidates)
    : IDocumentRetriever
{
    public const int DefaultCandidates = 20;

    public int Candidates { get; } = candidates > 0 ? candidates : throw new ArgumentOutOfRangeException(nameof(candidates));

    public async Task<IReadOnlyList<RetrievedChunk>> SearchAsync(
        string query, int top = 5, RetrievalMode mode = RetrievalMode.Hybrid, CancellationToken cancellationToken = default)
    {
        var pool = await inner.SearchAsync(query, Math.Max(top, Candidates), mode, cancellationToken).ConfigureAwait(false);
        if (pool.Count == 0)
        {
            return pool;
        }

        // The contextual text (document > section > article) is what was indexed; the reranker sees the same.
        var scores = await reranker.ScoreAsync(query, [.. pool.Select(r => r.Chunk.ContextualText)], cancellationToken).ConfigureAwait(false);
        if (scores.Count != pool.Count)
        {
            throw new InvalidOperationException($"Reranker returned {scores.Count} scores for {pool.Count} passages.");
        }

        return [.. pool
            .Select((r, i) => (Result: r, Score: scores[i], Index: i))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Index)
            .Take(top)
            .Select(x => x.Result with { Score = x.Score })];
    }
}
