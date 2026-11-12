using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Assistant.Core.Retrieval;
using Assistant.Rag.Search;

namespace Assistant.Core.Models;

/// <summary>
/// Rerank scores on disk, so the eval runner and CI can measure re-ranking without an API key (step 3.4). Same
/// rules as <see cref="DiskEmbeddingCache"/>: below the PII masking layer, no text stored (the key is a SHA-256 of
/// model, query and passage), one JSONL file per model, new entries appended with "\n".
/// Without an inner reranker (offline, or no API key) a missing pair is an error.
/// </summary>
internal sealed class DiskRerankCache : IReranker
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private readonly string _modelId;
    private readonly Lazy<IReranker>? _inner;
    private readonly ConcurrentDictionary<string, double> _entries = new(StringComparer.Ordinal);
    private readonly Lock _writeLock = new();

    public DiskRerankCache(string directory, string modelId, Func<IReranker>? innerFactory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        _modelId = modelId;
        _inner = innerFactory is null ? null : new Lazy<IReranker>(innerFactory);
        FilePath = Path.Combine(directory, FileName(modelId));
        Load();
    }

    public string FilePath { get; }

    /// <summary>"nvidia/nemotron-3.5-rerank-8b" → "nvidia_nemotron_3_5_rerank_8b.jsonl".</summary>
    internal static string FileName(string modelId) =>
        RagServiceCollectionExtensions.CollectionName(modelId, 0)["chunks_".Length..^"_0".Length] + ".jsonl";

    internal static string Key(string modelId, string query, string passage) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{modelId}\n{query}\n{passage}")));

    public async Task<IReadOnlyList<double>> ScoreAsync(string query, IReadOnlyList<string> passages, CancellationToken cancellationToken = default)
    {
        var keys = passages.Select(p => Key(_modelId, query, p)).ToList();
        var missing = Enumerable.Range(0, passages.Count).Where(i => !_entries.ContainsKey(keys[i])).DistinctBy(i => keys[i]).ToList();

        if (missing.Count > 0)
        {
            if (_inner is null)
            {
                throw new InvalidOperationException(
                    $"{missing.Count} rerank score(s) missing from the cache ({FilePath}). Run once with a NIM API key and without offline mode to fill it.");
            }

            // A cross-encoder scores each (query, passage) pair on its own, so scoring only the missing passages gives
            // the same logits as scoring the whole list.
            var scores = await _inner.Value.ScoreAsync(query, [.. missing.Select(i => passages[i])], cancellationToken).ConfigureAwait(false);
            var lines = new StringBuilder();
            for (int j = 0; j < missing.Count; j++)
            {
                _entries[keys[missing[j]]] = scores[j];
                lines.Append(JsonSerializer.Serialize(new Entry(keys[missing[j]], scores[j]), Json)).Append('\n');
            }

            lock (_writeLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.AppendAllText(FilePath, lines.ToString());
            }
        }

        return [.. keys.Select(k => _entries[k])];
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
            _entries[entry.Key] = entry.Score;
        }
    }

    private sealed record Entry(string Key, double Score);
}
