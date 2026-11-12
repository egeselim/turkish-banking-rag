using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Assistant.Rag.Search;

namespace Assistant.Core.Models;

/// <summary>
/// NIM reranking endpoint (<c>{RerankEndpoint}{model}/reranking</c>). Schema verified with a probe on 2026-11-12:
/// request <c>{model, query: {text}, passages: [{text}], truncate}</c>, response <c>{rankings: [{index, logit}]}</c>.
/// The planned llama-nemotron-rerank-1b-v2 answered 410 Gone (end of life 2026-08-25); see ADR-003.
/// Returns logits in input order. Error messages carry the status and a truncated body, never the key.
/// </summary>
internal sealed class NimReranker(HttpClient http, Uri endpoint, string apiKey, string modelId) : IReranker
{
    private const int MaxErrorBodyLength = 300;

    private readonly Uri _uri = new(NimClientFactory.EnsureTrailingSlash(endpoint), modelId.Trim('/') + "/reranking");

    public async Task<IReadOnlyList<double>> ScoreAsync(string query, IReadOnlyList<string> passages, CancellationToken cancellationToken = default)
    {
        if (passages.Count == 0)
        {
            return [];
        }

        var body = new RerankRequest(modelId, new TextItem(query), [.. passages.Select(p => new TextItem(p))], "END");
        using var request = new HttpRequestMessage(HttpMethod.Post, _uri) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            string error = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException(
                $"NIM rerank request failed ({(int)response.StatusCode}): {(error.Length <= MaxErrorBodyLength ? error : error[..MaxErrorBodyLength] + "…")}",
                inner: null,
                response.StatusCode);
        }

        var payload = await response.Content.ReadFromJsonAsync<RerankResponse>(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("NIM rerank response was empty.");

        var scores = new double?[passages.Count];
        foreach (var ranking in payload.Rankings)
        {
            if (ranking.Index < 0 || ranking.Index >= scores.Length || scores[ranking.Index] is not null)
            {
                throw new InvalidOperationException($"NIM rerank returned an invalid or duplicate index {ranking.Index}.");
            }

            scores[ranking.Index] = ranking.Logit;
        }

        return scores.Any(s => s is null)
            ? throw new InvalidOperationException("NIM rerank did not score every passage.")
            : [.. scores.Select(s => s!.Value)];
    }

    private sealed record TextItem([property: JsonPropertyName("text")] string Text);

    private sealed record RerankRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("query")] TextItem Query,
        [property: JsonPropertyName("passages")] TextItem[] Passages,
        [property: JsonPropertyName("truncate")] string Truncate);

    private sealed record RerankResponse([property: JsonPropertyName("rankings")] Ranking[] Rankings);

    private sealed record Ranking(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("logit")] double Logit);
}
