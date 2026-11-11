using Assistant.Core.Models;
using Assistant.Rag;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Assistant.Core.Tests.Models;

public sealed class DiskEmbeddingCacheTests : IDisposable
{
    private const string Model = "test/embed";
    private const int Dimensions = 8;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "embedding-cache-" + Guid.NewGuid().ToString("N"));
    private readonly CountingGenerator _inner = new();

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task Second_request_is_served_from_disk_without_calling_the_model()
    {
        using (var first = Cache(_inner))
        {
            await first.GenerateAsync(["EFT ücreti", "FAST"], EmbeddingInputType.ForQuery(), TestContext.Current.CancellationToken);
        }

        using var second = Cache(innerFactory: null); // offline: a miss would throw
        var result = await second.GenerateAsync(["FAST", "EFT ücreti"], EmbeddingInputType.ForQuery(), TestContext.Current.CancellationToken);

        Assert.Equal(2, _inner.Texts.Count);
        Assert.Equal(CountingGenerator.VectorFor("FAST"), result[0].Vector.ToArray());
        Assert.Equal(CountingGenerator.VectorFor("EFT ücreti"), result[1].Vector.ToArray());
        Assert.Equal(2, second.GetService<EmbeddingCacheStats>()!.Hits);
    }

    [Fact]
    public async Task Only_missing_texts_reach_the_model_once_each()
    {
        using var cache = Cache(_inner);
        var ct = TestContext.Current.CancellationToken;

        await cache.GenerateAsync(["a"], EmbeddingInputType.ForPassage(), ct);
        var result = await cache.GenerateAsync(["a", "b", "b"], EmbeddingInputType.ForPassage(), ct);

        Assert.Equal(["a", "b"], _inner.Texts);
        Assert.Equal(3, result.Count);
        var stats = cache.GetService<EmbeddingCacheStats>()!;
        Assert.Equal(2, stats.Added);
        Assert.Equal(1, stats.Hits);
    }

    [Fact]
    public async Task Query_and_passage_of_the_same_text_are_different_entries()
    {
        using var cache = Cache(_inner);
        var ct = TestContext.Current.CancellationToken;

        await cache.GenerateAsync(["EFT"], EmbeddingInputType.ForQuery(), ct);
        await cache.GenerateAsync(["EFT"], EmbeddingInputType.ForPassage(), ct);

        Assert.Equal(2, _inner.Texts.Count);
        Assert.NotEqual(
            DiskEmbeddingCache.Key(Model, Dimensions, EmbeddingInputType.Query, "EFT"),
            DiskEmbeddingCache.Key(Model, Dimensions, EmbeddingInputType.Passage, "EFT"));
    }

    [Fact]
    public async Task Offline_miss_fails_and_names_the_count()
    {
        using var cache = Cache(innerFactory: null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            cache.GenerateAsync(["x", "y"], EmbeddingInputType.ForQuery(), TestContext.Current.CancellationToken));

        Assert.StartsWith("2 embedding(s) missing", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cache_file_holds_hashes_not_texts()
    {
        using var cache = Cache(_inner);
        await cache.GenerateAsync(["Kartım çalındı"], EmbeddingInputType.ForQuery(), TestContext.Current.CancellationToken);

        string file = await File.ReadAllTextAsync(cache.FilePath, TestContext.Current.CancellationToken);

        Assert.DoesNotContain("çalındı", file, StringComparison.Ordinal); // not in the base64 alphabet, so no false match
        Assert.Contains(DiskEmbeddingCache.Key(Model, Dimensions, EmbeddingInputType.Query, "Kartım çalındı"), file, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", file, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_input_type_is_rejected()
    {
        using var cache = Cache(_inner);

        await Assert.ThrowsAsync<ArgumentException>(() => cache.GenerateAsync(["x"], cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void File_name_follows_model_and_size() =>
        Assert.Equal("nvidia_nemotron_3_5_embed_8b_4096.jsonl", DiskEmbeddingCache.FileName("nvidia/nemotron-3.5-embed-8b", 4096));

    // Through the real DI chain: no API key and offline, the generator still resolves and masks PII before the cache key.
    [Fact]
    public async Task Di_with_cache_and_no_key_resolves_and_keeps_the_masking_wrapper()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Nim:ChatModel"] = "c",
            ["Nim:SmallChatModel"] = "s",
            ["Nim:EmbeddingModel"] = Model,
            ["Nim:EmbeddingDimensions"] = Dimensions.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Nim:EmbeddingCachePath"] = _directory,
            ["Nim:EmbeddingCacheOffline"] = "true",
        }).Build();
        using var services = new ServiceCollection().AddNimModels(configuration).BuildServiceProvider();

        var generator = services.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();

        Assert.Equal("PiiMaskingEmbeddingGenerator", generator.GetType().Name);
        Assert.NotNull(generator.GetService<EmbeddingCacheStats>());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            generator.GenerateAsync(["soru"], EmbeddingInputType.ForQuery(), TestContext.Current.CancellationToken));
    }

    private DiskEmbeddingCache Cache(CountingGenerator? innerFactory) =>
        new(_directory, Model, Dimensions, innerFactory is null ? null : () => innerFactory);

    private sealed class CountingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public List<string> Texts { get; } = [];

        public static float[] VectorFor(string text) =>
            [.. System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)).Take(Dimensions).Select(b => b / 7f)];

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
        {
            var list = values.ToList();
            Texts.AddRange(list);
            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(list.Select(v => new Embedding<float>(VectorFor(v)))));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
