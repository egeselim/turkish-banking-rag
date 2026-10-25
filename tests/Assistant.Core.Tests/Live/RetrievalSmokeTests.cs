using System.Globalization;
using System.Text;
using Assistant.Core.Models;
using Assistant.Core.Retrieval;
using Assistant.Rag;
using Assistant.Rag.Chunking;
using Assistant.Rag.Documents;
using Assistant.Rag.Search;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;
using Qdrant.Client;
using Testcontainers.Qdrant;

namespace Assistant.Core.Tests.Live;

/// <summary>
/// First retrieval measurement (step 1.5, ADR-002): real NIM embeddings, real Qdrant (Docker), the real
/// source PDFs and 10 hand-written questions phrased the way customers ask, not copied from the documents.
/// Compares vector-only, BM25-only, our hybrid (RRF) and the Qdrant connector's built-in hybrid search.
/// Report: artifacts/retrieval/smoke.md. Replaced by the eval runner in step 3.2.
/// </summary>
[Trait("Category", "Live")]
public sealed class RetrievalSmokeTests : IAsyncLifetime
{
    // Any listed (document, label) counts as a correct source.
    private static readonly (string Question, (string Doc, string Label)[] Accept)[] Questions =
    [
        ("Başka bankaya mobilden para göndermek kaç lira?", [("ucret-tarifesi", "Madde 4"), ("sss", "Soru 10")]),
        ("Gece yarısı acil para yollamam lazım, anında gitmesi için ne kullanmalıyım?", [("ucret-tarifesi", "Madde 5"), ("sss", "Soru 10"), ("sss", "Soru 11")]),
        ("Kartım çalındı, ben fark edene kadar yapılan harcamalardan ne kadar sorumluyum?", [("kredi-karti-sozlesmesi", "Madde 8"), ("sss", "Soru 1")]),
        ("Ekstremde tanımadığım bir harcama var, itiraz için ne kadar vaktim var?", [("kredi-karti-sozlesmesi", "Madde 9"), ("sss", "Soru 6")]),
        ("Platinum kartın senelik ücreti ne kadar?", [("ucret-tarifesi", "Madde 7")]),
        ("Asgariyi ödeyemezsem başıma ne gelir?", [("kredi-karti-sozlesmesi", "Madde 13"), ("sss", "Soru 8")]),
        ("Başka bankanın bankamatiğinden para çekersem kesinti olur mu?", [("ucret-tarifesi", "Madde 9"), ("sss", "Soru 12")]),
        ("Bilgilerim yurt dışındaki bir şirkete gönderilebilir mi?", [("kvkk-6698", "Madde 9")]),
        ("Açık rıza ne anlama geliyor?", [("kvkk-6698", "Madde 3")]),
        ("Tatile gittim, kartım dışarıda çalışmıyor", [("kredi-karti-sozlesmesi", "Madde 6"), ("sss", "Soru 5")]),
    ];

    private readonly QdrantContainer _qdrant = new QdrantBuilder("qdrant/qdrant:v1.18.0").Build();
    private ServiceProvider _services = null!;

    public async ValueTask InitializeAsync()
    {
        LiveSettings.RequireApiKey();
        await _qdrant.StartAsync();
        _services = new ServiceCollection()
            .AddNimModels(LiveSettings.Configuration)
            .AddSingleton(new QdrantClient(new Uri(_qdrant.GetGrpcConnectionString())))
            .AddRag()
            .BuildServiceProvider();
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        await _qdrant.DisposeAsync();
    }

    [Fact]
    public async Task Compare_retrieval_methods_on_hand_written_questions()
    {
        var ct = TestContext.Current.CancellationToken;
        var chunks = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Sources"), "*.pdf")
            .Order(StringComparer.Ordinal)
            .SelectMany(p => new StructureAwareChunker().Chunk(PdfDocumentReader.Read(p)))
            .ToList();
        var index = _services.GetRequiredService<ChunkIndex>();
        await index.SyncAsync(chunks, ct);

        var retriever = _services.GetRequiredService<HybridRetriever>();
        var embedder = _services.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
        var connector = (IKeywordHybridSearchable<ChunkRecord>)index.Collection;

        var methods = new (string Name, Func<string, Task<List<DocumentChunk>>> Search)[]
        {
            ("Vektör", async q => [.. (await retriever.SearchAsync(q, 5, RetrievalMode.VectorOnly, ct)).Select(r => r.Chunk)]),
            ("BM25", async q => [.. (await retriever.SearchAsync(q, 5, RetrievalMode.KeywordOnly, ct)).Select(r => r.Chunk)]),
            ("Hybrid (bizim RRF)", async q => [.. (await retriever.SearchAsync(q, 5, RetrievalMode.Hybrid, ct)).Select(r => r.Chunk)]),
            ("Connector hybrid", async q =>
            {
                var vector = (await embedder.GenerateAsync([q], EmbeddingInputType.ForQuery(), ct))[0].Vector;
                var keywords = TurkishTextNormalizer.Terms(q).Distinct().ToList();
                var hits = new List<DocumentChunk>();
                await foreach (var r in connector.HybridSearchAsync(vector, keywords, 5, cancellationToken: ct))
                {
                    hits.Add(r.Record.ToChunk());
                }

                return hits;
            }),
        };

        var report = new StringBuilder("# Retrieval smoke (1.5)\n\n");
        report.Append(CultureInfo.InvariantCulture, $"{chunks.Count} chunk, {Questions.Length} soru, embedding: {LiveSettings.Configuration["Nim:EmbeddingModel"]}\n\n");
        report.Append("| Yöntem | Hit@1 | Hit@5 | MRR |\n|---|---|---|---|\n");
        var details = new StringBuilder("\n## Soru bazında doğru kaynağın sırası (- = ilk 5'te yok)\n\n| Soru | " +
            string.Join(" | ", methods.Select(m => m.Name)) + " |\n|---|" + string.Concat(methods.Select(_ => "---|")) + "\n");
        var ranks = new int?[Questions.Length, methods.Length];
        var topHits = new StringBuilder("\n## İlk 3 sonuç (doküman:etiket)\n\n");
        var hybridHit5 = 0.0;

        for (int m = 0; m < methods.Length; m++)
        {
            int hit1 = 0, hit5 = 0;
            double mrr = 0;
            for (int q = 0; q < Questions.Length; q++)
            {
                var results = await methods[m].Search(Questions[q].Question);
                topHits.Append("- ").Append(methods[m].Name).Append(" | ").Append(Questions[q].Question).Append(" → ")
                    .Append(string.Join("; ", results.Take(3).Select(c => $"{c.DocId}:{c.Label ?? "-"}"))).Append('\n');
                int index0 = results.FindIndex(c => Questions[q].Accept.Contains((c.DocId, c.Label ?? "")));
                ranks[q, m] = index0 < 0 ? null : index0 + 1;
                hit1 += index0 == 0 ? 1 : 0;
                hit5 += index0 >= 0 ? 1 : 0;
                mrr += index0 < 0 ? 0 : 1.0 / (index0 + 1);
            }

            report.Append(CultureInfo.InvariantCulture,
                $"| {methods[m].Name} | {hit1}/{Questions.Length} | {hit5}/{Questions.Length} | {mrr / Questions.Length:F2} |\n");
            if (m == 2)
            {
                hybridHit5 = (double)hit5 / Questions.Length;
            }
        }

        for (int q = 0; q < Questions.Length; q++)
        {
            var row = Enumerable.Range(0, methods.Length).Select(m => ranks[q, m]?.ToString(CultureInfo.InvariantCulture) ?? "-");
            details.Append("| ").Append(Questions[q].Question).Append(" | ").Append(string.Join(" | ", row)).Append(" |\n");
        }

        await File.WriteAllTextAsync(Path.Combine(LiveSettings.ArtifactsDirectory("retrieval"), "smoke.md"), report.Append(details).Append(topHits).ToString(), ct);

        // Floor, not a target: guards against regressions until the real eval set exists (3.1).
        Assert.True(hybridHit5 >= 0.8, $"Hybrid Hit@5 {hybridHit5:P0} below 80%. See artifacts/retrieval/smoke.md.");
    }
}
