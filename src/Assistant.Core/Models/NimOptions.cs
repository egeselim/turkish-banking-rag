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
