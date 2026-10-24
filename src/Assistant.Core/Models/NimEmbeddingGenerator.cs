using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Assistant.Rag;
using Microsoft.Extensions.AI;

namespace Assistant.Core.Models;

/// <summary>
/// Calls the NIM embeddings endpoint directly. The OpenAI SDK has no supported way to send
/// NIM's required <c>input_type</c> field, and asymmetric NIM models reject requests without it.
/// </summary>
internal sealed class NimEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private const int MaxErrorBodyLength = 500;

    private readonly HttpClient _http;
    private readonly Uri _embeddingsUri;
    private readonly string _apiKey;
    private readonly string _modelId;
    private readonly EmbeddingGeneratorMetadata _metadata;

    public NimEmbeddingGenerator(HttpClient http, Uri endpoint, string apiKey, string modelId)
    {
        _http = http;
        _embeddingsUri = new Uri(endpoint, "embeddings");
        _apiKey = apiKey;
        _modelId = modelId;
        _metadata = new EmbeddingGeneratorMetadata("nvidia-nim", endpoint, modelId, defaultModelDimensions: null);
    }

    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        string inputType = GetInputType(options);
        var body = new EmbeddingRequest(options?.ModelId ?? _modelId, [.. values], inputType, options?.Dimensions);

        using var request = new HttpRequestMessage(HttpMethod.Post, _embeddingsUri) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            string error = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException(
                $"NIM embeddings request failed ({(int)response.StatusCode}): {Truncate(error)}",
                inner: null,
                response.StatusCode);
        }

        var payload = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("NIM embeddings response was empty.");

        var embeddings = payload.Data
            .OrderBy(d => d.Index)
            .Select(d => new Embedding<float>(d.Embedding) { ModelId = payload.Model });

        return new GeneratedEmbeddings<Embedding<float>>(embeddings)
        {
            Usage = payload.Usage is null ? null : new UsageDetails
            {
                InputTokenCount = payload.Usage.PromptTokens,
                TotalTokenCount = payload.Usage.TotalTokens,
            },
        };
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is not null ? null
        : serviceType == typeof(EmbeddingGeneratorMetadata) ? _metadata
        : serviceType.IsInstanceOfType(this) ? this
        : null;

    public void Dispose()
    {
        // HttpClient is owned by the caller (IHttpClientFactory).
    }

    private static string GetInputType(EmbeddingGenerationOptions? options)
    {
        if (options?.AdditionalProperties?.TryGetValue(EmbeddingInputType.PropertyName, out object? value) == true
            && value is EmbeddingInputType.Query or EmbeddingInputType.Passage)
        {
            return (string)value;
        }

        // Fail loudly: a silent default would quietly degrade retrieval quality.
        throw new ArgumentException(
            $"Embedding calls must declare '{EmbeddingInputType.PropertyName}' (query or passage). Use EmbeddingInputType.ForQuery()/ForPassage().",
            nameof(options));
    }

    private static string Truncate(string text) =>
        text.Length <= MaxErrorBodyLength ? text : text[..MaxErrorBodyLength] + "…";

    private sealed record EmbeddingRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] string[] Input,
        [property: JsonPropertyName("input_type")] string InputType,
        [property: JsonPropertyName("dimensions"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Dimensions);

    private sealed record EmbeddingResponse(
        [property: JsonPropertyName("model")] string? Model,
        [property: JsonPropertyName("data")] EmbeddingData[] Data,
        [property: JsonPropertyName("usage")] EmbeddingUsage? Usage);

    private sealed record EmbeddingData(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("embedding")] float[] Embedding);

    private sealed record EmbeddingUsage(
        [property: JsonPropertyName("prompt_tokens")] int? PromptTokens,
        [property: JsonPropertyName("total_tokens")] int? TotalTokens);
}
