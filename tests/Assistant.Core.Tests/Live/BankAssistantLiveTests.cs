using System.Text;
using Assistant.Core.Audit;
using Assistant.Core.Banking;
using Assistant.Core.Chat;
using Assistant.Core.Models;
using Assistant.Core.Retrieval;
using Assistant.Core.Tests.Banking;
using Assistant.Core.Tests.Safety;
using Assistant.Rag.Search;
using Microsoft.Extensions.DependencyInjection;
using Qdrant.Client;
using SelimBank.Api.Tests;
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
    private readonly SelimBankApiFactory _api = new();
    private readonly InMemoryAuditLog _audit = new();
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
            .AddInMemorySelimBank(_api)
            .AddSingleton<IAuditLog>(_audit)
            .AddAuditLog(LiveSettings.Configuration)
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
        await _api.DisposeAsync();
    }

    [Fact]
    public async Task Answers_from_the_fee_tariff_and_cites_it()
    {
        var session = await _assistant.CreateSessionAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (answer, used) = await AskAsync(session, "Başka bankaya mobilden para göndermek kaç lira?");

        Assert.Contains("6,50", answer, StringComparison.Ordinal);
        Assert.Contains(used, s => s.DocId == "ucret-tarifesi" && s.Label == "Madde 4");
    }

    [Fact]
    public async Task Says_it_does_not_know_when_the_documents_do_not_answer()
    {
        var session = await _assistant.CreateSessionAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (answer, _) = await AskAsync(session, "Selim Bank'ın genel müdürü kim?");

        Assert.Contains("bulamadım", answer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Follow_up_question_uses_the_conversation()
    {
        var session = await _assistant.CreateSessionAsync(cancellationToken: TestContext.Current.CancellationToken);
        await AskAsync(session, "Başka bankaya mobilden para göndermek kaç lira?");

        var (answer, _) = await AskAsync(session, "Peki şubeden yaparsam?");

        Assert.Contains("32", answer, StringComparison.Ordinal);
    }

    // Step 2.2 acceptance: "son 3 harcamam" then "ikincisi" in the same session, through the real tools and API.
    [Fact]
    public async Task Lists_the_last_three_spendings_and_resolves_the_second_one()
    {
        const int customerId = 1;
        var expected = await LatestSpendingAsync(customerId, 3);
        var session = await _assistant.CreateSessionAsync(customerId, TestContext.Current.CancellationToken);

        var (list, _) = await AskAsync(session, "Son 3 harcamam neler?");
        var (second, _) = await AskAsync(session, "İkincisi neydi?");

        Assert.All(expected, t => Assert.Contains(AbsoluteAmount(t), list, StringComparison.Ordinal));
        Assert.Contains(AbsoluteAmount(expected[1]), second, StringComparison.Ordinal);
        Assert.DoesNotContain(AbsoluteAmount(expected[0]), second, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Answers_the_balance_of_the_session_customer()
    {
        const int customerId = 2;
        var bank = _services.GetRequiredService<SelimBankClient>();
        var accounts = await bank.GetAccountsAsync(customerId, TestContext.Current.CancellationToken);
        var session = await _assistant.CreateSessionAsync(customerId, TestContext.Current.CancellationToken);

        var (answer, _) = await AskAsync(session, "Bakiyem ne kadar?");

        Assert.All(accounts, a => Assert.Contains(BankingTools.Money(a.Balance, a.Currency).Replace(" TL", "", StringComparison.Ordinal), answer, StringComparison.Ordinal));
    }

    // Step 2.3 acceptance with the real model: the card is blocked only after approval.
    [Fact]
    public async Task Blocks_a_card_only_after_approval()
    {
        var session = await _assistant.CreateSessionAsync(1, TestContext.Current.CancellationToken);

        var (_, turn) = await AskTurnAsync(session, "9280 ile biten kartımı bloke et.");
        var approval = Assert.Single(turn.Approvals);
        Assert.Equal(BankingTools.BlockCardName, approval.ToolName);
        Assert.Contains("9280", approval.Summary, StringComparison.Ordinal);
        Assert.Equal(SelimBank.Api.Data.CardStatus.Active, _api.Query(db => db.Cards.Single(c => c.Id == 1).Status));

        await RespondAsync(session, approval, approve: true);

        Assert.Equal(SelimBank.Api.Data.CardStatus.Blocked, _api.Query(db => db.Cards.Single(c => c.Id == 1).Status));

        // Step 2.4 acceptance with the real model: the approved call left one masked audit row, under a pseudonym.
        var row = Assert.Single(_audit.Entries, e => e.Action == BankingTools.BlockCardName);
        Assert.Equal(AuditOutcome.Succeeded, row.Outcome);
        Assert.Matches("^[0-9a-f]{16}$", row.CustomerPseudonym);
        Assert.All(_audit.Entries, e => Assert.DoesNotContain(_api.Query(db => db.Cards.Single(c => c.Id == 1).Number), e.MaskedArguments, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Opens_a_complaint_for_the_second_spending_after_approval()
    {
        const int customerId = 1;
        var expected = await LatestSpendingAsync(customerId, 3);
        var session = await _assistant.CreateSessionAsync(customerId, TestContext.Current.CancellationToken);
        await AskAsync(session, "Son 3 harcamam neler?");

        var (_, turn) = await AskTurnAsync(session, "İkincisine itiraz etmek istiyorum, o harcamayı ben yapmadım.");
        var approval = Assert.Single(turn.Approvals);
        Assert.Equal(BankingTools.CreateComplaintName, approval.ToolName);
        Assert.Empty(_api.Query(db => db.Complaints.ToList()));

        await RespondAsync(session, approval, approve: true);

        var complaint = Assert.Single(_api.Query(db => db.Complaints.ToList()));
        Assert.Equal(expected[1].Id, complaint.TransactionId);
    }

    private async Task<(string Answer, AssistantTurn Turn)> AskTurnAsync(AssistantSession session, string question)
    {
        var turn = await _assistant.AskAsync(session, question, TestContext.Current.CancellationToken);
        string answer = await ReadAsync(turn);
        _transcript.Append("## ").Append(question).Append("\n\n").Append(answer).Append('\n')
            .Append(string.Concat(turn.Approvals.Select(a => $"\n> Onay ({a.ToolName}): {a.Summary}"))).Append("\n\n");
        return (answer, turn);
    }

    private async Task RespondAsync(AssistantSession session, PendingApproval approval, bool approve)
    {
        string answer = await ReadAsync(await _assistant.RespondAsync(session, approval.Id, approve, TestContext.Current.CancellationToken));
        _transcript.Append("## [").Append(approve ? "Onayla" : "Reddet").Append("]\n\n").Append(answer).Append("\n\n");
    }

    private static async Task<string> ReadAsync(AssistantTurn turn)
    {
        var answer = new StringBuilder();
        await foreach (string part in turn.Text)
        {
            answer.Append(part);
        }

        return answer.ToString();
    }

    private async Task<IReadOnlyList<BankTransaction>> LatestSpendingAsync(int customerId, int count)
    {
        var bank = _services.GetRequiredService<SelimBankClient>();
        var all = new List<BankTransaction>();
        foreach (var account in await bank.GetAccountsAsync(customerId, TestContext.Current.CancellationToken))
        {
            all.AddRange((await bank.GetTransactionsAsync(
                customerId, account.Id, count, TransactionDirection.Spending, TestContext.Current.CancellationToken))!);
        }

        return [.. all.OrderByDescending(t => t.Date).ThenByDescending(t => t.Id).Take(count)];
    }

    // The model may drop the minus sign ("176,11 TL harcama"); the digits must match the tool result.
    private static string AbsoluteAmount(BankTransaction t) =>
        BankingTools.Money(Math.Abs(t.Amount), t.Currency).Replace(" TL", "", StringComparison.Ordinal);

    private async Task<(string Answer, IReadOnlyList<CitedSource> Used)> AskAsync(AssistantSession session, string question)
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
