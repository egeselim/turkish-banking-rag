using System.Runtime.CompilerServices;
using System.Text.Json;
using Assistant.Core.Safety;
using Assistant.Privacy;
using Assistant.Privacy.Tests.TestData;
using Microsoft.Extensions.AI;

namespace Assistant.Core.Tests.Safety;

public sealed class PiiMaskingChatClientTests : IDisposable
{
    private readonly string _tckn;
    private readonly string _card;
    private readonly string _iban;
    private readonly RecordingChatClient _inner = new();
    private readonly PiiMaskingChatClient _client;

    public PiiMaskingChatClientTests()
    {
        var r = FakePii.Seeded();
        _tckn = FakePii.Tckn(r);
        _card = FakePii.CardNumber(r);
        _iban = FakePii.TrIban(r);
        _client = new PiiMaskingChatClient(_inner, new PiiMasker());
    }

    [Fact]
    public async Task Masks_user_and_system_text_and_instructions()
    {
        await _client.GetResponseAsync(
            [new(ChatRole.System, $"Müşteri TCKN: {_tckn}"), new(ChatRole.User, $"Kartım {_card} çalındı")],
            new ChatOptions { Instructions = $"IBAN {_iban} için işlem yapma" },
            TestContext.Current.CancellationToken);

        Assert.Equal("Müşteri TCKN: [TCKN]", _inner.Messages[0].Text);
        Assert.Equal("Kartım [KART_NO] çalındı", _inner.Messages[1].Text);
        Assert.Equal("IBAN [IBAN] için işlem yapma", _inner.Options!.Instructions);
        AssertNoRawPii(_inner.SentText());
    }

    [Fact]
    public async Task Masks_tool_call_arguments_and_tool_results()
    {
        using var json = JsonDocument.Parse($$"""{"iban":"{{_iban}}","amount":125.5}""");
        ChatMessage[] history =
        [
            new(ChatRole.Assistant, [new FunctionCallContent("c1", "ListTransactions", new Dictionary<string, object?>
            {
                ["cardNumber"] = _card,
                ["owner"] = json.RootElement.Clone(),
                ["count"] = 3,
            })]),
            new(ChatRole.Tool, [new FunctionResultContent("c1", $"Hesap sahibi TCKN {_tckn}")]),
            new(ChatRole.Tool, [new FunctionResultContent("c2", new { Iban = _iban, Balance = 1250.75m })]),
        ];

        await _client.GetResponseAsync(history, cancellationToken: TestContext.Current.CancellationToken);

        var call = Assert.IsType<FunctionCallContent>(_inner.Messages[0].Contents.Single());
        Assert.Equal("[KART_NO]", call.Arguments!["cardNumber"]);
        Assert.Equal(3, call.Arguments["count"]); // values without PII keep their type
        var result = Assert.IsType<FunctionResultContent>(_inner.Messages[1].Contents.Single());
        Assert.Equal("Hesap sahibi TCKN [TCKN]", result.Result);
        AssertNoRawPii(_inner.SentText());
    }

    [Fact]
    public async Task Never_mutates_the_callers_history()
    {
        var message = new ChatMessage(ChatRole.User, $"TCKN {_tckn}");

        await _client.GetResponseAsync([message], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal($"TCKN {_tckn}", message.Text);
    }

    [Fact]
    public async Task Drops_raw_representations_that_could_bypass_masking()
    {
        var message = new ChatMessage(ChatRole.User, [new TextContent($"TCKN {_tckn}") { RawRepresentation = $"raw {_tckn}" }])
        {
            RawRepresentation = $"raw {_tckn}",
        };

        await _client.GetResponseAsync([message], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(_inner.Messages[0].RawRepresentation);
        Assert.Null(_inner.Messages[0].Contents[0].RawRepresentation);
    }

    [Fact]
    public async Task Streaming_requests_are_masked_too()
    {
        await foreach (var _ in _client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, $"IBAN {_iban}")], cancellationToken: TestContext.Current.CancellationToken))
        {
        }

        Assert.Equal("IBAN [IBAN]", _inner.Messages[0].Text);
    }

    public void Dispose() => _client.Dispose();

    private void AssertNoRawPii(string sent)
    {
        Assert.DoesNotContain(_tckn, sent, StringComparison.Ordinal);
        Assert.DoesNotContain(_card, sent, StringComparison.Ordinal);
        Assert.DoesNotContain(_iban, sent, StringComparison.Ordinal);
    }

    /// <summary>Stands in for the provider client and records exactly what would leave the process.</summary>
    internal sealed class RecordingChatClient : IChatClient
    {
        public List<ChatMessage> Messages { get; private set; } = [];

        public ChatOptions? Options { get; private set; }

        public string SentText() => string.Join('\n',
            Messages.SelectMany(m => m.Contents).Select(c => c switch
            {
                TextContent t => t.Text,
                FunctionCallContent f => JsonSerializer.Serialize(f.Arguments, AIJsonUtilities.DefaultOptions),
                FunctionResultContent r => JsonSerializer.Serialize(r.Result, AIJsonUtilities.DefaultOptions),
                _ => c.ToString(),
            }).Append(Options?.Instructions ?? ""));

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Record(messages, options);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "tamam")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Record(messages, options);
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "tamam");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }

        private void Record(IEnumerable<ChatMessage> messages, ChatOptions? options)
        {
            Messages = [.. messages];
            Options = options;
        }
    }
}
