using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Assistant.Core.Models;
using Assistant.Core.Retrieval;
using Assistant.Core.Safety;
using Assistant.Core.Tests.Safety;
using Assistant.Privacy;
using Assistant.Rag.Search;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Qdrant.Client;

namespace Assistant.Core.Tests.Models;

public sealed class RerankerTests : IDisposable
{
    private const string Model = "nvidia/test-rerank";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "rerank-cache-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task Nim_request_has_the_probed_shape_and_scores_come_back_in_input_order()
    {
        var handler = new ScriptedHandler(HttpStatusCode.OK, """{"rankings":[{"index":1,"logit":2.5},{"index":0,"logit":-1.0}]}""");
        var reranker = new NimReranker(new HttpClient(handler), new Uri("https://ai.api.nvidia.com/v1/retrieval"), "fake-api-key-not-real", Model);

        var scores = await reranker.ScoreAsync("EFT?", ["aidat", "EFT 6,50 TL"], TestContext.Current.CancellationToken);

        Assert.Equal([-1.0, 2.5], scores);
        Assert.Equal("https://ai.api.nvidia.com/v1/retrieval/nvidia/test-rerank/reranking", handler.Uri!.AbsoluteUri);
        var body = JsonNode.Parse(handler.Body!)!;
        Assert.Equal(Model, body["model"]!.GetValue<string>());
        Assert.Equal("EFT?", body["query"]!["text"]!.GetValue<string>());
        Assert.Equal("EFT 6,50 TL", body["passages"]![1]!["text"]!.GetValue<string>());
        Assert.Equal("END", body["truncate"]!.GetValue<string>());
    }

    [Fact]
    public async Task Nim_error_names_the_status_but_not_the_key()
    {
        var handler = new ScriptedHandler(HttpStatusCode.Gone, """{"title":"Gone"}""");
        var reranker = new NimReranker(new HttpClient(handler), new Uri("https://x.test/"), "fake-api-key-not-real", Model);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => reranker.ScoreAsync("q", ["p"], TestContext.Current.CancellationToken));

        Assert.Contains("410", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("fake-api-key-not-real", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nim_response_missing_a_passage_is_rejected()
    {
        var handler = new ScriptedHandler(HttpStatusCode.OK, """{"rankings":[{"index":0,"logit":1.0}]}""");
        var reranker = new NimReranker(new HttpClient(handler), new Uri("https://x.test/"), "k", Model);

        await Assert.ThrowsAsync<InvalidOperationException>(() => reranker.ScoreAsync("q", ["a", "b"], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Cache_serves_known_pairs_from_disk_and_asks_only_for_new_ones()
    {
        var inner = new CountingReranker();
        await new DiskRerankCache(_directory, Model, () => inner).ScoreAsync("q", ["a", "b"], TestContext.Current.CancellationToken);

        var offline = new DiskRerankCache(_directory, Model, innerFactory: null);
        Assert.Equal([1.0, 2.0], await offline.ScoreAsync("q", ["a", "b"], TestContext.Current.CancellationToken));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => offline.ScoreAsync("q", ["a", "c"], TestContext.Current.CancellationToken));
        Assert.StartsWith("1 rerank score(s) missing", ex.Message, StringComparison.Ordinal);

        var online = new DiskRerankCache(_directory, Model, () => inner);
        await online.ScoreAsync("q", ["a", "c"], TestContext.Current.CancellationToken);
        Assert.Equal(["a", "b", "c"], inner.Seen);
        Assert.DoesNotContain("\"a\"", await File.ReadAllTextAsync(online.FilePath, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Masking_wrapper_masks_query_and_passages()
    {
        var inner = new CountingReranker();
        var masker = new PiiMasker();
        var pii = SyntheticPii.NationalId();
        var reranker = new PiiMaskingReranker(inner, masker);

        await reranker.ScoreAsync($"TC {pii}", [$"metin {pii}"], TestContext.Current.CancellationToken);

        Assert.Equal(["metin [TCKN]"], inner.Seen);
        Assert.Equal("TC [TCKN]", inner.LastQuery);
    }

    [Fact]
    public void Factory_returns_only_masking_wrappers_and_works_offline_without_a_key()
    {
        using var sp = Services(new() { ["Nim:RerankCachePath"] = _directory, ["Nim:RerankCacheOffline"] = "true" });

        Assert.IsType<PiiMaskingReranker>(sp.GetRequiredService<RerankerFactory>().Create(Model));
    }

    [Fact]
    public void Rag_rerank_model_wraps_the_retriever_and_mode_reaches_options()
    {
        using var plain = Services(new() { ["Nim:ApiKey"] = "k" });
        using var reranked = Services(new() { ["Rag:RerankModel"] = Model, ["Rag:Mode"] = "VectorOnly", ["Nim:ApiKey"] = "k" });

        Assert.IsType<HybridRetriever>(plain.GetRequiredService<IDocumentRetriever>());
        Assert.IsType<RerankingRetriever>(reranked.GetRequiredService<IDocumentRetriever>());
        Assert.Equal(RetrievalMode.VectorOnly, reranked.GetRequiredService<Microsoft.Extensions.Options.IOptions<RagOptions>>().Value.Mode);
    }

    [Fact]
    public void Cache_file_name_follows_the_model() =>
        Assert.Equal("nvidia_nemotron_3_5_rerank_8b.jsonl", DiskRerankCache.FileName("nvidia/nemotron-3.5-rerank-8b"));

    private static ServiceProvider Services(Dictionary<string, string?> values)
    {
        values.TryAdd("Nim:ChatModel", "c");
        values.TryAdd("Nim:SmallChatModel", "s");
        values.TryAdd("Nim:EmbeddingModel", "e");
        values.TryAdd("Nim:EmbeddingDimensions", "8");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new ServiceCollection()
            .AddNimModels(configuration)
            .AddSingleton(new QdrantClient("localhost"))
            .AddRag()
            .AddSourceIngestion(configuration)
            .BuildServiceProvider();
    }

    private sealed class CountingReranker : IReranker
    {
        public List<string> Seen { get; } = [];

        public string? LastQuery { get; private set; }

        public Task<IReadOnlyList<double>> ScoreAsync(string query, IReadOnlyList<string> passages, CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            Seen.AddRange(passages);
            return Task.FromResult<IReadOnlyList<double>>([.. passages.Select(p => (double)(p[0] - 'a' + 1))]);
        }
    }

    private sealed class ScriptedHandler(HttpStatusCode status, string response) : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
}
