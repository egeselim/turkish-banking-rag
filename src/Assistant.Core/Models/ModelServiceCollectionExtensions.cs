using Assistant.Core.Safety;
using Assistant.Privacy;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Assistant.Core.Models;

public static class ModelServiceCollectionExtensions
{
    /// <summary>DI key of the small chat model used for classification and routing.</summary>
    public const string SmallChatClientKey = "small";

    /// <summary>
    /// Registers the main <see cref="IChatClient"/>, the keyed small one (<see cref="SmallChatClientKey"/>)
    /// and the <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/>, all backed by NVIDIA NIM.
    /// Only PII-masking wrappers are registered; the raw clients never enter DI.
    /// </summary>
    public static IServiceCollection AddNimModels(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<NimOptions>()
            .Bind(configuration.GetSection(NimOptions.SectionName))
            .PostConfigure(options =>
            {
                if (string.IsNullOrWhiteSpace(options.ApiKey))
                {
                    options.ApiKey = configuration["NVIDIA_API_KEY"];
                }
            });

        // Resilience is configured per client here, not host-wide (see SelimBank.ServiceDefaults).
        //
        // Chat: retries come from the OpenAI SDK (NimOptions.MaxRetries); an HTTP retry layer would multiply
        // them, and a short attempt timeout would cut long answers.
        services.AddHttpClient(NimClientFactory.HttpClientName, (sp, http) =>
            http.Timeout = sp.GetRequiredService<IOptions<NimOptions>>().Value.RequestTimeout);

        // Embeddings answer in under a second, but free-tier requests occasionally hang indefinitely.
        // Ingestion makes many calls, so cut a stuck attempt early and retry instead of failing the whole sync.
        services.AddHttpClient(NimClientFactory.EmbeddingHttpClientName)
            .AddStandardResilienceHandler(o =>
            {
                o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(20);
                o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(40); // must be >= 2 x attempt timeout
                o.Retry.MaxRetryAttempts = 5; // exponential backoff 2-4-8-16-32 s rides out short NIM outages (HTTP 500)
                o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(90);
            });

        services.TryAddSingleton<IPiiMasker, PiiMasker>();

        services.AddChatClient(sp => CreateChatClient(sp, o => o.ChatModel));
        services.AddKeyedChatClient(SmallChatClientKey, sp => CreateChatClient(sp, o => o.SmallChatModel));
        services.AddEmbeddingGenerator(sp =>
        {
            var (options, http) = Resolve(sp, NimClientFactory.EmbeddingHttpClientName);
            var raw = NimClientFactory.CreateEmbeddingGenerator(options, options.EmbeddingModel, http);
            return new PiiMaskingEmbeddingGenerator(raw, sp.GetRequiredService<IPiiMasker>());
        });

        return services;
    }

    private static PiiMaskingChatClient CreateChatClient(IServiceProvider sp, Func<NimOptions, string> modelSelector)
    {
        var (options, http) = Resolve(sp, NimClientFactory.HttpClientName);
        var raw = NimClientFactory.CreateChatClient(options, modelSelector(options), http);
        return new PiiMaskingChatClient(raw, sp.GetRequiredService<IPiiMasker>());
    }

    private static (NimOptions Options, HttpClient Http) Resolve(IServiceProvider sp, string httpClientName)
    {
        var options = sp.GetRequiredService<IOptions<NimOptions>>().Value;
        options.Validate();
        var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(httpClientName);
        return (options, http);
    }
}
