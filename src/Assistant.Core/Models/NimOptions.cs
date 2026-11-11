namespace Assistant.Core.Models;

/// <summary>
/// NVIDIA NIM connection settings. Model ids live only in configuration (see ADR-001), never in code.
/// The API key comes from user-secrets (<c>Nim:ApiKey</c>) or the <c>NVIDIA_API_KEY</c> environment variable.
/// </summary>
public sealed class NimOptions
{
    public const string SectionName = "Nim";

    public Uri Endpoint { get; set; } = new("https://integrate.api.nvidia.com/v1/");

    public string? ApiKey { get; set; }

    public string ChatModel { get; set; } = "";

    public string SmallChatModel { get; set; } = "";

    public string EmbeddingModel { get; set; } = "";

    /// <summary>Vector size of <see cref="EmbeddingModel"/>; part of the vector collection name.</summary>
    public int EmbeddingDimensions { get; set; }

    /// <summary>
    /// Folder of the on-disk embedding cache (<c>Nim:EmbeddingCachePath</c>). Unset in the app; the eval runner sets
    /// it to <c>eval/cache/embeddings</c> so retrieval can be measured without an API key.
    /// </summary>
    public string? EmbeddingCachePath { get; set; }

    /// <summary>With the cache on: never call NIM; a text missing from the cache is an error.</summary>
    public bool EmbeddingCacheOffline { get; set; }

    /// <summary>
    /// Sends <c>enable_thinking=false</c> with every chat request (<c>Nim:DisableThinking</c>). Needed for the
    /// nemotron-3.5 backup model, whose reasoning otherwise fills the output budget (ADR-001, 2026-11-11).
    /// </summary>
    public bool DisableThinking { get; set; }

    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(120);

    /// <summary>Chat SDK retries per request. Kept low: a stuck free-tier model would otherwise block for minutes.</summary>
    public int MaxRetries { get; set; } = 2;

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException("NIM API key is missing. Set 'Nim:ApiKey' in user-secrets or the NVIDIA_API_KEY environment variable.");
        }

        if (string.IsNullOrWhiteSpace(ChatModel) || string.IsNullOrWhiteSpace(SmallChatModel) || string.IsNullOrWhiteSpace(EmbeddingModel))
        {
            throw new InvalidOperationException("NIM model ids must be configured: Nim:ChatModel, Nim:SmallChatModel, Nim:EmbeddingModel.");
        }
    }

    internal void ValidateEmbeddingDimensions()
    {
        if (EmbeddingDimensions <= 0)
        {
            throw new InvalidOperationException("Nim:EmbeddingDimensions must be set to the vector size of Nim:EmbeddingModel.");
        }
    }
}
