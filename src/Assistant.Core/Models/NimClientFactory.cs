using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using OpenAI;

namespace Assistant.Core.Models;

/// <summary>
/// Creates the raw (unwrapped) NIM clients. Internal on purpose: application code resolves clients from DI,
/// where they are wrapped by the privacy decorators (step 1.3). Only this assembly and its tests build raw clients.
/// </summary>
internal static class NimClientFactory
{
    public const string HttpClientName = "nim";

    public static IChatClient CreateChatClient(NimOptions options, string modelId, HttpClient http)
    {
        var client = new OpenAIClient(
            new ApiKeyCredential(options.ApiKey!),
            new OpenAIClientOptions
            {
                Endpoint = EnsureTrailingSlash(options.Endpoint),
                Transport = new HttpClientPipelineTransport(http),
                NetworkTimeout = options.RequestTimeout,
                RetryPolicy = new ClientRetryPolicy(options.MaxRetries),
            });

        return client.GetChatClient(modelId).AsIChatClient();
    }

    public static IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(NimOptions options, string modelId, HttpClient http) =>
        new NimEmbeddingGenerator(http, EnsureTrailingSlash(options.Endpoint), options.ApiKey!, modelId);

    // Without the trailing slash, relative paths would replace the "/v1" segment.
    internal static Uri EnsureTrailingSlash(Uri uri) =>
        uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
}
