using System.Collections.Concurrent;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Assistant.Core.Audit;
using Assistant.Core.Banking;
using Assistant.Core.Chat;
using Assistant.Core.Models;
using Assistant.Core.Tests.Banking;
using Assistant.Core.Tests.Safety;
using Assistant.Privacy.Tests.TestData;
using Assistant.Rag;
using Assistant.Rag.Search;
using Bogus;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using SelimBank.Api.Tests;

namespace Assistant.Core.Tests.Telemetry;

/// <summary>
/// Step 2.5 acceptance (PLAN §5): canary PII goes through a whole conversation (question, tool call with approval,
/// tool run, answer) on the real composition: ServiceDefaults telemetry with its OTLP exporter, the NIM SDK, the
/// masking and OpenTelemetry layers, the agent, audit. Only the outer ends are fake: the NIM server (HTTP) and the
/// telemetry receiver (OTLP/HTTP). No canary may appear in what goes to NIM, in any log (at Trace level), in any
/// exported span, log or metric, or in the audit log. The NIM key is checked the same way.
/// </summary>
public sealed class TelemetryLeakTests
{
    private const string ApiKey = "test-key-not-real";

    [Fact]
    public async Task Canary_pii_never_reaches_the_model_logs_telemetry_or_audit()
    {
        var random = FakePii.Seeded(2025);
        var faker = new Faker("tr") { Random = random };
        string tckn = FakePii.Tckn(random);
        string card = FakePii.CardNumber(random);
        string iban = FakePii.TrIban(random);
        string phone = "0" + FakePii.Mobile(random);
        string email = FakePii.Email(faker);
        string[] canaries = [tckn, card, iban, phone, email, ApiKey];

        // The model is scripted to misbehave: it puts raw PII into tool arguments and into its answer.
        var nim = new FakeNimHandler(
            FakeNimHandler.ToolCall(BankingTools.CreateComplaintName, new { description = $"Kart {card}, TCKN {tckn} ile ilgili itiraz" }),
            FakeNimHandler.Text("Şikâyetiniz açıldı. Kart numaranız ", card[..7], card[7..], " olarak kayıtlı."));
        var logs = new CapturingLoggerProvider();
        var audit = new InMemoryAuditLog();
        await using var otlp = await FakeOtlpReceiver.StartAsync();
        await using var bank = new SelimBankApiFactory();

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Development" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Nim:ApiKey"] = ApiKey,
            ["Nim:Endpoint"] = "http://fake-nim.test/v1/",
            ["Nim:ChatModel"] = "test/chat",
            ["Nim:SmallChatModel"] = "test/small",
            ["Nim:EmbeddingModel"] = "test/embed",
            ["Nim:EmbeddingDimensions"] = "4",
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = otlp.Endpoint.ToString(),
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
        });
        builder.ConfigureOpenTelemetry();
        builder.Logging.SetMinimumLevel(LogLevel.Trace).AddProvider(logs);
        builder.Services
            .AddNimModels(builder.Configuration)
            .AddInMemorySelimBank(bank)
            .AddSingleton<IAuditLog>(audit)
            .AddAuditLog(builder.Configuration)
            .AddSingleton<IDocumentRetriever, EmbeddingOnlyRetriever>()
            .AddBankAssistant();
        builder.Services.AddHttpClient(NimClientFactory.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => nim);
        builder.Services.AddHttpClient(NimClientFactory.EmbeddingHttpClientName).ConfigurePrimaryHttpMessageHandler(() => nim);

        string reply;
        using (var host = builder.Build())
        {
            await host.StartAsync(Ct);
            var assistant = host.Services.GetRequiredService<BankAssistant>();
            var session = await assistant.CreateSessionAsync(1, Ct);

            var turn = await assistant.AskAsync(
                session, $"Kartım {card} ile ilgili şikâyet açmak istiyorum. TCKN {tckn}, IBAN {iban}, telefon {phone}, e-posta {email}.", Ct);
            await ReadAsync(turn);
            var approval = Assert.Single(turn.Approvals);
            Assert.DoesNotContain(canaries, c => approval.Summary.Contains(c, StringComparison.Ordinal));

            reply = await ReadAsync(await assistant.RespondAsync(session, approval.Id, approve: true, Ct));

            Assert.True(host.Services.GetRequiredService<TracerProvider>().ForceFlush(10_000), "trace flush");
            Assert.True(host.Services.GetRequiredService<LoggerProvider>().ForceFlush(10_000), "log flush");
            Assert.True(host.Services.GetRequiredService<MeterProvider>().ForceFlush(10_000), "metric flush");
            await host.StopAsync(Ct);
        } // disposing the host shuts the providers down, which flushes every exporter

        // The pipeline really ran: masked text reached NIM, telemetry arrived, the tool ran and was audited.
        string toNim = string.Join("\n", nim.Requests.Select(r => r.Body));
        Assert.Contains(nim.Requests, r => r.Path.EndsWith("/embeddings", StringComparison.Ordinal));
        Assert.True(nim.Requests.Count(r => r.Path.EndsWith("/chat/completions", StringComparison.Ordinal)) >= 2);
        Assert.Contains("[TCKN]", toNim, StringComparison.Ordinal);
        Assert.Contains("[KART_NO]", toNim, StringComparison.Ordinal);
        string traces = Encoding.UTF8.GetString(otlp.All("traces"));
        Assert.Contains("test/chat", traces, StringComparison.Ordinal);       // gen_ai chat span
        Assert.Contains(BankingTools.CreateComplaintName, traces, StringComparison.Ordinal); // execute_tool span
        Assert.True(otlp.All("logs").Length > 0, "signals: " + string.Join(",", otlp.Payloads.Select(p => p.Signal + ":" + p.Body.Length)) + " logs captured: " + logs.Entries.Count);
        Assert.Contains("gen_ai.client", Encoding.UTF8.GetString(otlp.All("metrics")), StringComparison.Ordinal);
        Assert.NotEmpty(logs.Entries);
        Assert.Equal(AuditOutcome.Succeeded, Assert.Single(audit.Entries).Outcome);
        Assert.Contains("[KART_NO]", reply, StringComparison.Ordinal);

        // No canary anywhere.
        AssertNoCanary(canaries, "NIM requests", toNim);
        AssertNoCanary(canaries, "logs (Trace)", string.Join("\n", logs.Entries));
        foreach (string signal in new[] { "traces", "logs", "metrics" })
        {
            AssertNoCanary(canaries, $"OTLP {signal}", Encoding.UTF8.GetString(otlp.All(signal)));
        }

        AssertNoCanary(canaries, "audit", string.Join("\n", audit.Entries));
        AssertNoCanary(canaries, "answer", reply);
    }

    /// <summary>Checks the canary as written, digits only, and JSON-escaped (default escaping turns '+' and '@' into \u...).</summary>
    private static void AssertNoCanary(string[] canaries, string where, string text)
    {
        foreach (string canary in canaries)
        {
            string digits = string.Concat(canary.Where(char.IsAsciiDigit));
            string[] forms =
            [
                canary,
                JsonEncodedText.Encode(canary).ToString(),
                JsonEncodedText.Encode(canary, JavaScriptEncoder.UnsafeRelaxedJsonEscaping).ToString(),
                .. digits.Length >= 10 ? [digits] : Array.Empty<string>(),
            ];
            foreach (string form in forms.Distinct())
            {
                Assert.False(text.Contains(form, StringComparison.Ordinal), $"A canary ({canary.Length} chars) leaked into {where}.");
            }
        }
    }

    private static async Task<string> ReadAsync(AssistantTurn turn)
    {
        var text = new StringBuilder();
        await foreach (string part in turn.Text)
        {
            text.Append(part);
        }

        return text.ToString();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Sends the question through the DI embedding generator (masking + telemetry), like the real retriever, and finds nothing.</summary>
    private sealed class EmbeddingOnlyRetriever(IEmbeddingGenerator<string, Embedding<float>> embeddings) : IDocumentRetriever
    {
        public async Task<IReadOnlyList<RetrievedChunk>> SearchAsync(
            string query, int top = 5, RetrievalMode mode = RetrievalMode.Hybrid, CancellationToken cancellationToken = default)
        {
            await embeddings.GenerateAsync([query], EmbeddingInputType.ForQuery(), cancellationToken);
            return [];
        }
    }

    /// <summary>Everything handed to ILogger: message, state values, exception and scopes.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider, ISupportExternalScope
    {
        private IExternalScopeProvider? _scopes;

        public ConcurrentQueue<string> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

        public void Dispose()
        {
        }

        private sealed class Logger(CapturingLoggerProvider owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => owner._scopes?.Push(state);

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var entry = new StringBuilder().Append(category).Append(' ').Append(formatter(state, exception));
                if (state is IEnumerable<KeyValuePair<string, object?>> values)
                {
                    foreach (var (key, value) in values)
                    {
                        entry.Append(' ').Append(key).Append('=').Append(value);
                    }
                }

                entry.Append(' ').Append(exception);
                owner._scopes?.ForEachScope((scope, sb) => sb.Append(" scope=").Append(scope), entry);
                owner.Entries.Enqueue(entry.ToString());
            }
        }
    }
}
