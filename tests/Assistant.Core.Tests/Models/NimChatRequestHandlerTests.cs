using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Assistant.Core.Models;

namespace Assistant.Core.Tests.Models;

public sealed class NimChatRequestHandlerTests
{
    private const string ChatUrl = "https://integrate.api.nvidia.com/v1/chat/completions";

    [Fact]
    public async Task Thinking_off_adds_the_template_argument_and_keeps_the_rest_of_the_body()
    {
        var sent = await SendAsync(disableThinking: true, ChatUrl, """{"model":"m","messages":[{"role":"user","content":"Kartım çalındı"}],"chat_template_kwargs":{"other":1}}""");

        var json = JsonNode.Parse(sent)!;
        Assert.False(json["chat_template_kwargs"]!["enable_thinking"]!.GetValue<bool>());
        Assert.Equal(1, json["chat_template_kwargs"]!["other"]!.GetValue<int>());
        Assert.Equal("m", json["model"]!.GetValue<string>());
        Assert.Equal("Kartım çalındı", json["messages"]![0]!["content"]!.GetValue<string>());
    }

    [Fact]
    public async Task Body_is_untouched_when_thinking_is_not_disabled()
    {
        const string body = """{"model":"m","messages":[]}""";

        Assert.Equal(body, await SendAsync(disableThinking: false, ChatUrl, body));
    }

    [Fact]
    public async Task Other_endpoints_are_untouched()
    {
        const string body = """{"model":"e","input":["x"],"input_type":"query"}""";

        Assert.Equal(body, await SendAsync(disableThinking: true, "https://integrate.api.nvidia.com/v1/embeddings", body));
    }

    private static async Task<string> SendAsync(bool disableThinking, string url, string body)
    {
        var capture = new CapturingHandler();
        using var handler = new NimChatRequestHandler(() => disableThinking) { InnerHandler = capture };
        using var client = new HttpClient(handler);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(new Uri(url), content, TestContext.Current.CancellationToken);

        return capture.Body!;
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
