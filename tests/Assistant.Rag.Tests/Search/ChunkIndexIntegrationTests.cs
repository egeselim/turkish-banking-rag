using Assistant.Rag.Chunking;
using Assistant.Rag.Search;

namespace Assistant.Rag.Tests.Search;

/// <summary>Ingestion and retrieval against a real Qdrant (Docker required).</summary>
[Trait("Category", "Integration")]
public sealed class ChunkIndexIntegrationTests(QdrantFixture qdrant) : IClassFixture<QdrantFixture>
{
    private static readonly IReadOnlyList<DocumentChunk> Chunks = QdrantFixture.SourceChunks();

    [Fact]
    public async Task Sync_is_idempotent_and_never_re_embeds_unchanged_chunks()
    {
        var ct = TestContext.Current.CancellationToken;
        var embedder = new HashingEmbeddingGenerator();
        using var collection = qdrant.NewCollection();
        var index = new ChunkIndex(collection, embedder);

        var first = await index.SyncAsync(Chunks, ct);
        var second = await index.SyncAsync(Chunks, ct);

        Assert.Equal(new IngestionResult(Chunks.Count, 0, 0), first);
        Assert.Equal(new IngestionResult(0, Chunks.Count, 0), second);
        Assert.Equal(Chunks.Count, embedder.EmbeddedTexts);
        Assert.All(embedder.InputTypes, t => Assert.Equal(EmbeddingInputType.Passage, t));
        Assert.Equal(Chunks.Count, await CountAsync(collection, ct));
    }

    [Fact]
    public async Task Sync_removes_chunks_that_disappeared_from_the_sources()
    {
        var ct = TestContext.Current.CancellationToken;
        using var collection = qdrant.NewCollection();
        var index = new ChunkIndex(collection, new HashingEmbeddingGenerator());
        await index.SyncAsync(Chunks, ct);

        var withoutFaq = Chunks.Where(c => c.DocId != "sss").ToList();
        var result = await index.SyncAsync(withoutFaq, ct);

        Assert.Equal(Chunks.Count - withoutFaq.Count, result.Removed);
        Assert.Equal(withoutFaq.Count, await CountAsync(collection, ct));
    }

    [Theory]
    [InlineData("EFT ücreti ne kadar?", "ucret-tarifesi", "Madde 4")]
    [InlineData("kartimi caldirdim ne yapmaliyim", "sss", "Soru 1")]
    [InlineData("asgari ödeme tutarını ödemezsem gecikme faizi", "kredi-karti-sozlesmesi", "Madde 13")]
    [InlineData("ilgili kişinin hakları", "kvkk-6698", "Madde 11")]
    public async Task Hybrid_search_finds_the_expected_article_in_the_top_5(string query, string docId, string label)
    {
        var ct = TestContext.Current.CancellationToken;
        var embedder = new HashingEmbeddingGenerator();
        using var collection = qdrant.NewCollection();
        var index = new ChunkIndex(collection, embedder);
        await index.SyncAsync(Chunks, ct);

        var hits = await new HybridRetriever(index, embedder).SearchAsync(query, top: 5, cancellationToken: ct);

        Assert.Contains(hits, h => h.Chunk.DocId == docId && h.Chunk.Label == label);
        Assert.Equal(EmbeddingInputType.Query, embedder.InputTypes[^1]);
    }

    [Fact]
    public async Task Stored_records_round_trip_to_the_original_chunks()
    {
        var ct = TestContext.Current.CancellationToken;
        using var collection = qdrant.NewCollection();
        var index = new ChunkIndex(collection, new HashingEmbeddingGenerator());
        await index.SyncAsync(Chunks, ct);

        var original = Chunks.First(c => c.Label == "Madde 8" && c.DocId == "kredi-karti-sozlesmesi");
        var stored = await collection.GetAsync(original.Id, cancellationToken: ct);

        var roundTripped = stored!.ToChunk();
        Assert.Equal(original.Text, roundTripped.Text);
        Assert.Equal(original.SectionPath, roundTripped.SectionPath);
        Assert.Equal((original.Label, original.PageStart, original.Part), (roundTripped.Label, roundTripped.PageStart, roundTripped.Part));
    }

    private static async Task<int> CountAsync(Microsoft.Extensions.VectorData.VectorStoreCollection<Guid, ChunkRecord> collection, CancellationToken ct)
    {
        int count = 0;
        await foreach (var _ in collection.GetAsync(r => r.Part >= 1, 100_000, cancellationToken: ct))
        {
            count++;
        }

        return count;
    }
}
