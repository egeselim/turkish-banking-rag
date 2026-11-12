using Assistant.Core.Safety;
using Assistant.Privacy;
using Assistant.Rag.Search;
using Microsoft.Extensions.Options;

namespace Assistant.Core.Models;

/// <summary>
/// Builds rerankers by model id (ids live in configuration only). Every reranker it returns is wrapped in
/// <see cref="PiiMaskingReranker"/>: the query is user input and goes to NIM. Chain: PII masking → (disk cache,
/// eval runner only) → raw NIM.
/// </summary>
public sealed class RerankerFactory
{
    public const string HttpClientName = "nim-rerank";

    private readonly IOptions<NimOptions> _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IPiiMasker _masker;

    internal RerankerFactory(IOptions<NimOptions> options, IHttpClientFactory httpClientFactory, IPiiMasker masker)
    {
        _options = options;
        _httpClientFactory = httpClientFactory;
        _masker = masker;
    }

    public IReranker Create(string modelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        var options = _options.Value;

        IReranker inner = string.IsNullOrWhiteSpace(options.RerankCachePath)
            ? CreateRaw(modelId)
            : new DiskRerankCache(
                options.RerankCachePath,
                modelId,
                options.RerankCacheOffline || string.IsNullOrWhiteSpace(options.ApiKey) ? null : () => CreateRaw(modelId));

        return new PiiMaskingReranker(inner, _masker);
    }

    private NimReranker CreateRaw(string modelId)
    {
        var options = _options.Value;
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException("NIM API key is missing. Set 'Nim:ApiKey' in user-secrets or the NVIDIA_API_KEY environment variable.");
        }

        return new NimReranker(_httpClientFactory.CreateClient(HttpClientName), options.RerankEndpoint, options.ApiKey, modelId);
    }
}
