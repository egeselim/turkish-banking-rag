using System.Security.Cryptography;
using System.Text;
using Assistant.Core.Models;
using Assistant.Core.Retrieval;
using Assistant.Rag.Search;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Qdrant.Client;
using Testcontainers.Qdrant;

namespace Eval.Runner.Tests;

/// <summary>
/// The evaluator on the app's real retrieval composition (AddRag, Qdrant in Docker, the real source PDFs) with a
/// deterministic fake embedding, so no API key is needed. Checks the plumbing, not retrieval quality.
/// </summary>
[Trait("Category", "Integration")]
public sealed class RetrievalEvaluatorIntegrationTests : IAsyncLifetime
{
    private const int Dimensions = 32;

    private readonly QdrantContainer _qdrant = new QdrantBuilder(RepoPaths.QdrantImage).Build();

    public async ValueTask InitializeAsync() => await _qdrant.StartAsync();

    public async ValueTask DisposeAsync() => await _qdrant.DisposeAsync();

    [Fact]
    public async Task Every_question_gets_a_result_for_every_mode()
    {
        var ct = TestContext.Current.CancellationToken;
        var services = new ServiceCollection();
        services.AddOptions<NimOptions>().Configure(o =>
        {
            o.EmbeddingModel = "fake/hash";
            o.EmbeddingDimensions = Dimensions;
        });
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(new HashEmbeddingGenerator());
        services.AddSingleton(new QdrantClient(new Uri(_qdrant.GetGrpcConnectionString())));
        services.AddRag();
        await using var provider = services.BuildServiceProvider();

        var chunks = SourceDocuments.LoadChunks(Path.Combine(AppContext.BaseDirectory, "Sources"));
        await provider.GetRequiredService<ChunkIndex>().SyncAsync(chunks, ct);
        var questions = QuestionSet.Load(Path.Combine(AppContext.BaseDirectory, "Eval", "questions.jsonl")).Where(q => q.Smoke).ToList();

        var hybrid = provider.GetRequiredService<HybridRetriever>();
        List<RetrievalMethod> methods =
        [
            .. RetrievalEvaluator.BaseMethods(hybrid),
            new("Hybrid+fake", new RerankingRetriever(hybrid, new ReverseReranker()), RetrievalMode.Hybrid, RetrievalMetrics.TopK),
        ];

        var results = await RetrievalEvaluator.RunAsync(methods, questions, ct);

        Assert.Equal(questions.Select(q => q.Id), results.Select(r => r.Id));
        Assert.All(results, r =>
        {
            Assert.Equal(methods.Count, r.LatencyMs.Count);
            Assert.Equal(3, r.HybridTop3.Count);
            Assert.Equal(r.Answerable ? methods.Count : 0, r.Ranks.Count);
            Assert.All(RetrievalReport.BaseMethods, m => Assert.True(r.Ranks.GetValueOrDefault(m) is null or (>= 1 and <= 20)));
            Assert.True(r.Ranks.GetValueOrDefault("Hybrid+fake") is null or (>= 1 and <= 5));
        });

        // BM25 does not depend on the fake vectors, so it must find something for these keyword-rich questions.
        var summaries = new RetrievalReport(1, DateTimeOffset.UtcNow, "smoke", "fake/hash", Dimensions, chunks.Count, 0, 0, results).Summaries();
        var bm25 = summaries.Single(s => s.Method == "KeywordOnly");
        Assert.True(bm25.Hit5 >= bm25.Questions / 2, $"BM25 Hit@5 {bm25.Hit5}/{bm25.Questions}");
        Assert.NotNull(bm25.Hit20);
        Assert.Null(summaries.Single(s => s.Method == "Hybrid+fake").Hit20);
    }

    /// <summary>Puts the last candidate first: enough to show the reranked method really reorders.</summary>
    private sealed class ReverseReranker : IReranker
    {
        public Task<IReadOnlyList<double>> ScoreAsync(string query, IReadOnlyList<string> passages, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<double>>([.. passages.Select((_, i) => (double)i)]);
    }

    private sealed class HashEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(values.Select(v =>
                new Embedding<float>(SHA256.HashData(Encoding.UTF8.GetBytes(v)).Select(b => b / 255f + 0.01f).ToArray())))); // 32 bytes = Dimensions

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
