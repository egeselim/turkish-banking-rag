using Assistant.Rag.Chunking;
using Microsoft.Extensions.AI;

namespace Assistant.Rag.Search;

public enum RetrievalMode
{
    Hybrid,
    VectorOnly,
    KeywordOnly,
}

/// <param name="VectorRank">1-based rank in the vector list, or null if not retrieved by vectors.</param>
/// <param name="KeywordRank">1-based rank in the BM25 list, or null if not retrieved by keywords.</param>
public sealed record RetrievedChunk(DocumentChunk Chunk, double Score, int? VectorRank, int? KeywordRank);

public sealed record RetrievalOptions
{
    /// <summary>Candidates taken from each of the vector and keyword lists before fusion.</summary>
    public int CandidatesPerList { get; init; } = 20;

    public int RrfK { get; init; } = ReciprocalRankFusion.DefaultK;
}

/// <summary>
/// Hybrid retrieval: vector search in the store and BM25 over Turkish-normalised terms, fused with RRF.
/// The store connector's own hybrid search is not used: its keyword side is an unranked filter.
/// See ADR-002.
/// </summary>
public sealed class HybridRetriever(
    ChunkIndex index,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    RetrievalOptions? options = null)
{
    private readonly RetrievalOptions _options = options ?? new RetrievalOptions();

    public async Task<IReadOnlyList<RetrievedChunk>> SearchAsync(
        string query, int top = 5, RetrievalMode mode = RetrievalMode.Hybrid, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var chunks = new Dictionary<Guid, DocumentChunk>();
        List<Guid> vectorIds = [];
        List<Guid> keywordIds = [];

        if (mode != RetrievalMode.KeywordOnly)
        {
            var embedding = await embeddingGenerator.GenerateAsync([query], EmbeddingInputType.ForQuery(), cancellationToken).ConfigureAwait(false);
            await foreach (var hit in index.Collection.SearchAsync(embedding[0].Vector, _options.CandidatesPerList, cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                vectorIds.Add(hit.Record.Id);
                chunks.TryAdd(hit.Record.Id, hit.Record.ToChunk());
            }
        }

        var keywords = index.Keywords;
        if (mode != RetrievalMode.VectorOnly && keywords is not null)
        {
            foreach (var (id, _) in keywords.Index.Search(query, _options.CandidatesPerList))
            {
                keywordIds.Add(id);
                chunks.TryAdd(id, keywords.Chunks[id]);
            }
        }

        return [.. ReciprocalRankFusion.Fuse([vectorIds, keywordIds], _options.RrfK)
            .Take(top)
            .Select(r => new RetrievedChunk(chunks[r.Id], r.Score, RankOf(vectorIds, r.Id), RankOf(keywordIds, r.Id)))];
    }

    private static int? RankOf(List<Guid> list, Guid id)
    {
        int i = list.IndexOf(id);
        return i < 0 ? null : i + 1;
    }
}
