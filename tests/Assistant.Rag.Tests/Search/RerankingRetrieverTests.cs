using Assistant.Rag.Chunking;
using Assistant.Rag.Search;

namespace Assistant.Rag.Tests.Search;

public sealed class RerankingRetrieverTests
{
    [Fact]
    public async Task Reorders_the_candidates_by_score_and_returns_the_top_ones()
    {
        var inner = new FixedRetriever(["a", "b", "c", "d"]);
        var retriever = new RerankingRetriever(inner, new MapReranker(new() { ["a"] = 0.1, ["b"] = 0.9, ["c"] = -1, ["d"] = 0.5 }), candidates: 4);

        var results = await retriever.SearchAsync("soru", top: 2, RetrievalMode.VectorOnly, TestContext.Current.CancellationToken);

        Assert.Equal(["b", "d"], results.Select(r => r.Chunk.Text));
        Assert.Equal(0.9, results[0].Score);
        Assert.Equal((4, RetrievalMode.VectorOnly), (inner.LastTop, inner.LastMode));
    }

    [Fact]
    public async Task Ties_keep_the_inner_order()
    {
        var retriever = new RerankingRetriever(new FixedRetriever(["a", "b", "c"]), new MapReranker(new() { ["a"] = 1, ["b"] = 1, ["c"] = 1 }));

        var results = await retriever.SearchAsync("soru", top: 3, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["a", "b", "c"], results.Select(r => r.Chunk.Text));
    }

    [Fact]
    public async Task The_reranker_sees_the_contextual_text_that_was_indexed()
    {
        var reranker = new MapReranker([]);
        var retriever = new RerankingRetriever(new FixedRetriever(["Mobil EFT: 6,50 TL."]), reranker);

        await retriever.SearchAsync("EFT", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Tarife > Madde 4\nMobil EFT: 6,50 TL.", Assert.Single(reranker.Seen));
    }

    [Fact]
    public async Task A_score_count_mismatch_fails_loudly()
    {
        var retriever = new RerankingRetriever(new FixedRetriever(["a", "b"]), new MapReranker([]) { Truncate = true });

        await Assert.ThrowsAsync<InvalidOperationException>(() => retriever.SearchAsync("q", cancellationToken: TestContext.Current.CancellationToken));
    }

    private sealed class FixedRetriever(string[] texts) : IDocumentRetriever
    {
        public int LastTop { get; private set; }

        public RetrievalMode LastMode { get; private set; }

        public Task<IReadOnlyList<RetrievedChunk>> SearchAsync(string query, int top = 5, RetrievalMode mode = RetrievalMode.Hybrid, CancellationToken cancellationToken = default)
        {
            (LastTop, LastMode) = (top, mode);
            return Task.FromResult<IReadOnlyList<RetrievedChunk>>([.. texts.Take(top).Select((t, i) => new RetrievedChunk(
                new DocumentChunk(Guid.NewGuid(), "doc", "Tarife", ["Madde 4"], "Madde 4", 1, 1, i, 1, 1, t), 1.0 / (i + 1), i + 1, null))]);
        }
    }

    private sealed class MapReranker(Dictionary<string, double> scores) : IReranker
    {
        public List<string> Seen { get; } = [];

        public bool Truncate { get; init; }

        public Task<IReadOnlyList<double>> ScoreAsync(string query, IReadOnlyList<string> passages, CancellationToken cancellationToken = default)
        {
            Seen.AddRange(passages);
            var result = passages.Select(p => scores.GetValueOrDefault(p.Split('\n')[^1])).ToList();
            return Task.FromResult<IReadOnlyList<double>>(Truncate ? result.Take(1).ToList() : result);
        }
    }
}
