using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Assistant.Core.Tests.Telemetry;

/// <summary>
/// Stands in for the NIM server at the HTTP level, so the real SDK, the masking client and the OpenTelemetry layer
/// all run. Records every request body (what would leave the machine) and answers chat requests from a script,
/// as server-sent events when the client streams.
/// </summary>
internal sealed class FakeNimHandler(params Func<JsonObject>[] script) : HttpMessageHandler
{
    private readonly Queue<Func<JsonObject>> _script = new(script);

    public ConcurrentQueue<(string Path, string Body)> Requests { get; } = new();

    /// <summary>An assistant message that calls one tool.</summary>
    public static Func<JsonObject> ToolCall(string name, object arguments) => () => new JsonObject
    {
        ["role"] = "assistant",
        ["tool_calls"] = new JsonArray(new JsonObject
        {
            ["index"] = 0,
            ["id"] = "call_1",
            ["type"] = "function",
            ["function"] = new JsonObject { ["name"] = name, ["arguments"] = JsonSerializer.Serialize(arguments) },
        }),
    };

    /// <summary>An assistant message; when streamed, each part is its own event (a value can be split across them).</summary>
    public static Func<JsonObject> Text(params string[] parts) => () => new JsonObject
    {
        ["role"] = "assistant",
        ["content"] = string.Concat(parts),
        ["parts"] = new JsonArray([.. parts.Select(p => (JsonNode)p)]),
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        string path = request.RequestUri!.AbsolutePath;
        Requests.Enqueue((path, body));

        if (path.EndsWith("/embeddings", StringComparison.Ordinal))
        {
            return Json(new JsonObject
            {
                ["model"] = "test/embed",
                ["data"] = new JsonArray(new JsonObject { ["index"] = 0, ["embedding"] = new JsonArray(0.1f, 0.2f, 0.3f, 0.4f) }),
                ["usage"] = new JsonObject { ["prompt_tokens"] = 3, ["total_tokens"] = 3 },
            });
        }

        var message = _script.Count > 0 ? _script.Dequeue()() : Text("tamam")();
        bool stream = JsonNode.Parse(body)?["stream"]?.GetValue<bool>() == true;
        return stream ? Stream(message) : Json(Completion(message));
    }

    private static JsonObject Completion(JsonObject message)
    {
        var parts = message["parts"];
        message.Remove("parts");
        _ = parts;
        return new JsonObject
        {
            ["id"] = "chatcmpl-1",
            ["object"] = "chat.completion",
            ["created"] = 1,
            ["model"] = "test/chat",
            ["choices"] = new JsonArray(new JsonObject
            {
                ["index"] = 0,
                ["message"] = message,
                ["finish_reason"] = message.ContainsKey("tool_calls") ? "tool_calls" : "stop",
            }),
            ["usage"] = Usage(),
        };
    }

    private static HttpResponseMessage Stream(JsonObject message)
    {
        var events = new List<JsonObject>();
        string finish;
        if (message["tool_calls"] is JsonArray calls)
        {
            events.Add(Chunk(new JsonObject { ["role"] = "assistant", ["tool_calls"] = calls.DeepClone() }));
            finish = "tool_calls";
        }
        else
        {
            foreach (var part in (JsonArray)message["parts"]!)
            {
                events.Add(Chunk(new JsonObject { ["role"] = "assistant", ["content"] = part!.GetValue<string>() }));
            }

            finish = "stop";
        }

        var last = Chunk(new JsonObject(), finish);
        last["usage"] = Usage();
        events.Add(last);

        var sse = new StringBuilder();
        foreach (var e in events)
        {
            sse.Append("data: ").Append(e.ToJsonString()).Append("\n\n");
        }

        sse.Append("data: [DONE]\n\n");
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sse.ToString(), Encoding.UTF8, "text/event-stream"),
        };
    }

    private static JsonObject Chunk(JsonObject delta, string? finish = null) => new()
    {
        ["id"] = "chatcmpl-1",
        ["object"] = "chat.completion.chunk",
        ["created"] = 1,
        ["model"] = "test/chat",
        ["choices"] = new JsonArray(new JsonObject { ["index"] = 0, ["delta"] = delta, ["finish_reason"] = finish }),
    };

    private static JsonObject Usage() => new() { ["prompt_tokens"] = 10, ["completion_tokens"] = 5, ["total_tokens"] = 15 };

    private static HttpResponseMessage Json(JsonObject body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
    };
}
