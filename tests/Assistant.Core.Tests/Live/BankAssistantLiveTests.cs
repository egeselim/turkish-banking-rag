using System.Text;
using Assistant.Core.Chat;
using Assistant.Core.Models;
using Assistant.Core.Retrieval;
using Assistant.Rag.Search;
using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;
using Qdrant.Client;
using Testcontainers.Qdrant;

namespace Assistant.Core.Tests.Live;

/// <summary>
/// Step 1.6 acceptance, end to end: real NIM models, real Qdrant (Docker), the real source PDFs,
/// the same DI composition as the web app. Answers are written to artifacts/chat/answers.md for review.
/// </summary>
[Trait("Category", "Live")]
public sealed class BankAssistantLiveTests : IAsyncLifetime
{
    private readonly QdrantContainer _qdrant = new QdrantBuilder("qdrant/qdrant:v1.18.0").Build();
    private readonly StringBuilder _transcript = new();
    private ServiceProvider _services = null!;
    private BankAssistant _assistant = null!;

    public async ValueTask InitializeAsync()
    {
        LiveSettings.RequireApiKey();
        await _qdrant.StartAsync();
        _services = new ServiceCollection()
            .AddNimModels(LiveSettings.Configuration)
            .AddSingleton(new QdrantClient(new Uri(_qdrant.GetGrpcConnectionString())))
            .AddRag()
            .AddBankAssistant()
            .BuildServiceProvider();

        var chunks = SourceIngestionService.LoadChunks(Path.Combine(AppContext.BaseDirectory, "Sources"));
        await _services.GetRequiredService<ChunkIndex>().SyncAsync(chunks, TestContext.Current.CancellationToken);
        _assistant = _services.GetRequiredService<BankAssistant>();
    }

    public async ValueTask DisposeAsync()
    {
        await File.AppendAllTextAsync(Path.Combine(LiveSettings.ArtifactsDirectory("chat"), "answers.md"), _transcript.ToString());
        await _services.DisposeAsync();
        await _qdrant.DisposeAsync();
    }

    [Fact]
    public async Task Answers_from_the_fee_tariff_and_cites_it()
    {
        var session = await _assistant.CreateSessionAsync(TestContext.Current.CancellationToken);

        var (answer, used) = await AskAsync(session, "Başka bankaya mobilden para göndermek kaç lira?");

        Assert.Contains("6,50", answer, StringComparison.Ordinal);
        Assert.Contains(used, s => s.DocId == "ucret-tarifesi" && s.Label == "Madde 4");
    }

    [Fact]
    public async Task Says_it_does_not_know_when_the_documents_do_not_answer()
    {
        var session = await _assistant.CreateSessionAsync(TestContext.Current.CancellationToken);

        var (answer, _) = await AskAsync(session, "Selim Bank'ın genel müdürü kim?");

        Assert.Contains("bulamadım", answer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Follow_up_question_uses_the_conversation()
    {
        var session = await _assistant.CreateSessionAsync(TestContext.Current.CancellationToken);
        await AskAsync(session, "Başka bankaya mobilden para göndermek kaç lira?");

        var (answer, _) = await AskAsync(session, "Peki şubeden yaparsam?");

        Assert.Contains("32", answer, StringComparison.Ordinal);
    }

    private async Task<(string Answer, IReadOnlyList<CitedSource> Used)> AskAsync(AgentSession session, string question)
    {
        var turn = await _assistant.AskAsync(session, question, TestContext.Current.CancellationToken);
        var answer = new StringBuilder();
        await foreach (string part in turn.Text)
        {
            answer.Append(part);
        }

        var used = Citations.UsedIn(answer.ToString(), turn.Sources);
        _transcript.Append("## ").Append(question).Append("\n\n").Append(answer).Append("\n\nKaynaklar: ")
            .Append(string.Join("; ", turn.Sources.Select(s => $"[{s.Number}] {s.DocId}:{s.Label}")))
            .Append("\n\n");
        return (answer.ToString(), used);
    }
}
