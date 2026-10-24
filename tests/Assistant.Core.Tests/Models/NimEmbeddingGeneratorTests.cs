using System.Net;
using System.Text;
using Assistant.Core.Models;
using Assistant.Rag;
using Microsoft.Extensions.AI;

namespace Assistant.Core.Tests.Models;

public class NimEmbeddingGeneratorTests
{
    private const string ApiKey = "nvapi-test-key-not-real";
    private static readonly Uri Endpoint = new("https://nim.example/v1/");

    [Fact]
    public async Task Sends_model_input_type_and_bearer_key_to_embeddings_path()
    {
        var handler = new RecordingHandler(_ => Json("""{"model":"m","data":[{"index":0,"embedding":[0.1,0.2]}]}"""));
        var generator = Create(handler);

        await generator.GenerateAsync(["merhaba"], EmbeddingInputType.ForQuery(), TestContext.Current.CancellationToken);

        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal("https://nim.example/v1/embeddings", request.RequestUri!.AbsoluteUri);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal(ApiKey, request.Headers.Authorization.Parameter);
        Assert.Contains("\"model\":\"test-model\"", body, StringComparison.Ordinal);
        Assert.Contains("\"input_type\":\"query\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("dimensions", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Returns_embeddings_in_input_order_even_if_response_is_shuffled()
    {
        var handler = new RecordingHandler(_ => Json(
            """{"model":"m","data":[{"index":1,"embedding":[2.0]},{"index":0,"embedding":[1.0]}],"usage":{"prompt_tokens":7,"total_tokens":7}}"""));
        var generator = Create(handler);

        var result = await generator.GenerateAsync(["a", "b"], EmbeddingInputType.ForPassage(), TestContext.Current.CancellationToken);

        Assert.Equal([1.0f, 2.0f], result.Select(e => e.Vector.Span[0]));
        Assert.Equal(7, result.Usage!.InputTokenCount);
    }

    [Fact]
    public async Task Throws_without_input_type_and_sends_nothing()
    {
        var handler = new RecordingHandler(_ => Json("{}"));
        var generator = Create(handler);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            generator.GenerateAsync(["a"], options: null, TestContext.Current.CancellationToken));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Error_includes_status_and_server_message_but_never_the_api_key()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"message":"'input_type' parameter is required"}""", Encoding.UTF8, "application/json"),
        });
        var generator = Create(handler);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            generator.GenerateAsync(["a"], EmbeddingInputType.ForQuery(), TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Contains("input_type", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ApiKey, ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://nim.example/v1", "https://nim.example/v1/")]
    [InlineData("https://nim.example/v1/", "https://nim.example/v1/")]
    public void Endpoint_gets_trailing_slash_so_v1_segment_is_kept(string input, string expected) =>
        Assert.Equal(expected, NimClientFactory.EnsureTrailingSlash(new Uri(input)).AbsoluteUri);

    private static NimEmbeddingGenerator Create(RecordingHandler handler) =>
        new(new HttpClient(handler), Endpoint, ApiKey, "test-model");

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request, body));
            return respond(request);
        }
    }
}
