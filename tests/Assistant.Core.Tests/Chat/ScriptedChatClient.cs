using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Assistant.Core.Tests.Chat;

/// <summary>
/// Stands in for the model: answers each call with the next scripted response (e.g. a tool call, then text)
/// and records every request, so tests can see exactly what the agent sent on each round trip.
/// </summary>
internal sealed class ScriptedChatClient(params ChatResponse[] script) : IChatClient
{
    private readonly Queue<ChatResponse> _script = new(script);

    public List<(IReadOnlyList<ChatMessage> Messages, ChatOptions? Options)> Calls { get; } = [];

    public static ChatResponse ToolCall(string callId, string name, Dictionary<string, object?>? arguments = null) =>
        new(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(callId, name, arguments ?? [])]));

    public static ChatResponse Text(string text) => new(new ChatMessage(ChatRole.Assistant, text));

    /// <summary>Serialized results of every tool call the model has seen in call <paramref name="call"/>.</summary>
    public IReadOnlyList<string> ToolResultsSentIn(int call) =>
    [
        .. Calls[call].Messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>()
            .Select(r => JsonSerializer.Serialize(r.Result, AIJsonUtilities.DefaultOptions)),
    ];

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(Next(messages, options));

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = Next(messages, options);
        await Task.Yield();
        foreach (var update in response.ToChatResponseUpdates())
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }

    private ChatResponse Next(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        Calls.Add(([.. messages], options));
        return _script.Count > 0 ? _script.Dequeue() : Text("tamam");
    }
}
