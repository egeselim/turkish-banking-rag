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
/// <remarks>
/// Batching limits come from measurements on the free tier, whose behaviour changes over time:
/// 2026-10-25, requests above ~5,500 characters hung without a response; 2026-11-04, any request with
/// more than one input failed at once with HTTP 500 (8b and 1b alike) while the same 101 chunks sent one
/// per request all succeeded. So inputs go one per request (<see cref="MaxInputsPerRequest"/>), and
/// <see cref="MaxCharsPerRequest"/> stays as a guard should batching be enabled again.
/// </remarks>
internal sealed class NimEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    internal const int MaxCharsPerRequest = 4000;

    internal const int MaxInputsPerRequest = 1;

    private const int MaxErrorBodyLength = 500;

    private readonly HttpClient _http;
    private readonly Uri _embeddingsUri;
    private readonly string _apiKey;
    private readonly string _modelId;
    private readonly EmbeddingGeneratorMetadata _metadata;
    private readonly int _maxInputsPerRequest;

    public NimEmbeddingGenerator(HttpClient http, Uri endpoint, string apiKey, string modelId, int maxInputsPerRequest = MaxInputsPerRequest)
    {
        _http = http;
        _embeddingsUri = new Uri(endpoint, "embeddings");
        _apiKey = apiKey;
        _modelId = modelId;
        _metadata = new EmbeddingGeneratorMetadata("nvidia-nim", endpoint, modelId, defaultModelDimensions: null);
        _maxInputsPerRequest = maxInputsPerRequest;
    }

    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        string inputType = GetInputType(options);
        var result = new GeneratedEmbeddings<Embedding<float>>();
        int? inputTokens = null, totalTokens = null;

        foreach (var batch in BatchBySize(values))
        {
            var (embeddings, usage) = await SendAsync(batch, inputType, options, cancellationToken).ConfigureAwait(false);
            result.AddRange(embeddings);
            if (usage is not null)
            {
                inputTokens = (inputTokens ?? 0) + (usage.PromptTokens ?? 0);
                totalTokens = (totalTokens ?? 0) + (usage.TotalTokens ?? 0);
            }
        }

        result.Usage = inputTokens is null ? null : new UsageDetails { InputTokenCount = inputTokens, TotalTokenCount = totalTokens };
        return result;
    }

    // Consecutive inputs within both limits; a single longer input goes alone.
    private IEnumerable<List<string>> BatchBySize(IEnumerable<string> values)
    {
        var batch = new List<string>();
        int chars = 0;
        foreach (string value in values)
        {
            if (batch.Count > 0 && (batch.Count >= _maxInputsPerRequest || chars + value.Length > MaxCharsPerRequest))
            {
                yield return batch;
                batch = [];
                chars = 0;
            }

            batch.Add(value);
            chars += value.Length;
        }

        if (batch.Count > 0)
        {
            yield return batch;
        }
    }

    private async Task<(IEnumerable<Embedding<float>> Embeddings, EmbeddingUsage? Usage)> SendAsync(
        List<string> values, string inputType, EmbeddingGenerationOptions? options, CancellationToken cancellationToken)
    {
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
            .Select(d => new Embedding<float>(d.Embedding) { ModelId = payload.Model })
            .ToList();

        return (embeddings, payload.Usage);
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
