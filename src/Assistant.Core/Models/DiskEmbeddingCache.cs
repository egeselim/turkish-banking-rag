using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Assistant.Core.Retrieval;
using Assistant.Rag;
using Microsoft.Extensions.AI;

namespace Assistant.Core.Models;

/// <summary>Counts for one process, reachable with <c>generator.GetService&lt;EmbeddingCacheStats&gt;()</c>.</summary>
public sealed class EmbeddingCacheStats
{
    private int _hits;
    private int _added;

    public int Hits => _hits;

    /// <summary>Embeddings fetched from NIM and written to the cache.</summary>
    public int Added => _added;

    internal void Hit(int count) => Interlocked.Add(ref _hits, count);

    internal void Add(int count) => Interlocked.Add(ref _added, count);
}

/// <summary>
/// Embedding cache on disk, so the eval runner can measure retrieval offline (step 3.2). Sits below the PII
/// masking layer, so it only ever sees masked text, and stores no text at all: the key is a SHA-256 of model,
/// dimensions, input type and text. One JSONL file per model and size, new entries appended (git-friendly).
/// Without an inner generator (offline, or no API key) a text missing from the cache is an error.
/// </summary>
internal sealed class DiskEmbeddingCache : IEmbeddingGenerator<string, Embedding<float>>
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private readonly string _modelId;
    private readonly int _dimensions;
    private readonly Lazy<IEmbeddingGenerator<string, Embedding<float>>>? _inner;
    private readonly ConcurrentDictionary<string, float[]> _entries = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly EmbeddingGeneratorMetadata _metadata;
    private readonly EmbeddingCacheStats _stats = new();

    public DiskEmbeddingCache(string directory, string modelId, int dimensions, Func<IEmbeddingGenerator<string, Embedding<float>>>? innerFactory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimensions);
        if (!BitConverter.IsLittleEndian)
        {
            throw new PlatformNotSupportedException("The embedding cache stores little-endian floats.");
        }

        _modelId = modelId;
        _dimensions = dimensions;
        _inner = innerFactory is null ? null : new Lazy<IEmbeddingGenerator<string, Embedding<float>>>(innerFactory);
        _metadata = new EmbeddingGeneratorMetadata("embedding-cache", defaultModelId: modelId, defaultModelDimensions: dimensions);
        FilePath = Path.Combine(directory, FileName(modelId, dimensions));
        Load();
    }

    public string FilePath { get; }

    /// <summary>"nvidia/nemotron-3.5-embed-8b", 4096 → "nvidia_nemotron_3_5_embed_8b_4096.jsonl".</summary>
    internal static string FileName(string modelId, int dimensions) =>
        RagServiceCollectionExtensions.CollectionName(modelId, dimensions)["chunks_".Length..] + ".jsonl";

    internal static string Key(string modelId, int dimensions, string inputType, string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{modelId}\n{dimensions}\n{inputType}\n{text}")));

    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        string inputType = InputType(options);
        var texts = values.ToList();
        var keys = texts.Select(t => Key(_modelId, _dimensions, inputType, t)).ToList();

        var cached = keys.Select(_entries.ContainsKey).ToList();
        var missing = Enumerable.Range(0, texts.Count)
            .Where(i => !cached[i])
            .DistinctBy(i => keys[i])
            .ToList();
        _stats.Hit(cached.Count(c => c)); // a text repeated within one request is fetched once but is not a hit

        if (missing.Count > 0)
        {
            await FetchAsync(missing.Select(i => (keys[i], texts[i])).ToList(), inputType, options, cancellationToken).ConfigureAwait(false);
        }

        return new GeneratedEmbeddings<Embedding<float>>(keys.Select(k => new Embedding<float>(_entries[k]) { ModelId = _modelId }));
    }

    private async Task FetchAsync(
        List<(string Key, string Text)> missing, string inputType, EmbeddingGenerationOptions? options, CancellationToken cancellationToken)
    {
        if (_inner is null)
        {
            throw new InvalidOperationException(
                $"{missing.Count} embedding(s) missing from the cache ({FilePath}). Run once with a NIM API key and without offline mode to fill it.");
        }

        var generated = await _inner.Value.GenerateAsync(missing.Select(m => m.Text), options, cancellationToken).ConfigureAwait(false);
        if (generated.Count != missing.Count)
        {
            throw new InvalidOperationException($"Expected {missing.Count} embeddings, got {generated.Count}.");
        }

        var lines = new List<string>(missing.Count);
        for (int i = 0; i < missing.Count; i++)
        {
            float[] vector = generated[i].Vector.ToArray();
            if (vector.Length != _dimensions)
            {
                throw new InvalidOperationException($"Embedding has {vector.Length} dimensions, expected {_dimensions}.");
            }

            lines.Add(JsonSerializer.Serialize(new Entry(missing[i].Key, inputType, Convert.ToBase64String(MemoryMarshal.AsBytes(vector.AsSpan()))), Json));
            _entries[missing[i].Key] = vector;
        }

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            // "\n" on every OS, so the committed file is the same wherever it was filled.
            await File.AppendAllTextAsync(FilePath, string.Concat(lines.Select(l => l + "\n")), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }

        _stats.Add(missing.Count);
    }

    private void Load()
    {
        if (!File.Exists(FilePath))
        {
            return;
        }

        foreach (string line in File.ReadLines(FilePath).Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            var entry = JsonSerializer.Deserialize<Entry>(line, Json) ?? throw new InvalidDataException($"Empty entry in {FilePath}.");
            float[] vector = MemoryMarshal.Cast<byte, float>(Convert.FromBase64String(entry.Vector)).ToArray();
            if (vector.Length != _dimensions)
            {
                throw new InvalidDataException($"Cache entry {entry.Key} has {vector.Length} dimensions, expected {_dimensions}.");
            }

            _entries[entry.Key] = vector;
        }
    }

    private static string InputType(EmbeddingGenerationOptions? options) =>
        options?.AdditionalProperties?.TryGetValue(EmbeddingInputType.PropertyName, out object? value) == true
        && value is EmbeddingInputType.Query or EmbeddingInputType.Passage
            ? (string)value
            : throw new ArgumentException(
                $"Embedding calls must declare '{EmbeddingInputType.PropertyName}' (query or passage).", nameof(options));

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is not null ? null
        : serviceType == typeof(EmbeddingCacheStats) ? _stats
        : serviceType == typeof(EmbeddingGeneratorMetadata) ? _metadata
        : serviceType.IsInstanceOfType(this) ? this
        : _inner is { IsValueCreated: true } ? _inner.Value.GetService(serviceType, serviceKey)
        : null;

    public void Dispose()
    {
        _writeGate.Dispose();
        if (_inner is { IsValueCreated: true })
        {
            _inner.Value.Dispose();
        }
    }

    private sealed record Entry(
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("input_type")] string InputType,
        [property: JsonPropertyName("vector")] string Vector);
}
