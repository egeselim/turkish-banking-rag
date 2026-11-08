using System.Text.Json;
using Assistant.Core.Banking;
using Assistant.Core.Chat;
using Assistant.Core.Tests.Banking;
using Assistant.Core.Tests.Safety;
using Assistant.Rag.Search;
using Microsoft.Extensions.AI;
using SelimBank.Api.Tests;
using static Assistant.Core.Tests.Chat.ScriptedChatClient;

namespace Assistant.Core.Tests.Chat;

// The model is scripted; the banking API is the real one in memory. Step 2.2 acceptance offline:
// "son 3 harcamam" then "ikincisi" in the same session, customer id from the session, never from the model.
public sealed class BankAssistantToolTests(SelimBankApiFactory api) : IClassFixture<SelimBankApiFactory>
{
    private const int Customer = 1;

    [Fact]
    public async Task Follow_up_sees_the_earlier_tool_result_in_the_same_session()
    {
        var expected = await LatestSpendingAsync(Customer, 3);
        using var model = new ScriptedChatClient(
            ToolCall("c1", "ListTransactions", new() { ["count"] = 3, ["kind"] = "spending" }),
            Text("1. ... 2. ... 3. ..."),
            Text("İkincisi ..."));
        var assistant = CreateAssistant(model);
        var session = await assistant.CreateSessionAsync(Customer, Ct);

        await DrainAsync(assistant, session, "Son 3 harcamam neler?");
        await DrainAsync(assistant, session, "İkincisi neydi?");

        Assert.Equal(3, model.Calls.Count);
        string toolResult = Assert.Single(model.ToolResultsSentIn(1));
        using var json = JsonDocument.Parse(toolResult);
        var listed = json.RootElement.GetProperty("transactions").EnumerateArray().ToList();
        Assert.Equal(expected.Select(t => t.Id), listed.Select(t => t.GetProperty("transactionId").GetInt32()));
        Assert.Equal([1, 2, 3], listed.Select(t => t.GetProperty("number").GetInt32()));

        // Turn 2 carries the turn-1 tool result in the history, so "ikincisi" can be resolved.
        Assert.Equal(toolResult, Assert.Single(model.ToolResultsSentIn(2)));
        Assert.Contains(model.Calls[2].Messages, m => m.Role == ChatRole.User && m.Text == "İkincisi neydi?");
    }

    [Fact]
    public async Task Tools_take_the_customer_from_the_session_not_from_the_model()
    {
        using var model = new ScriptedChatClient(ToolCall("c1", "GetBalance"), Text("tamam"));
        var assistant = CreateAssistant(model);

        await DrainAsync(assistant, await assistant.CreateSessionAsync(3, Ct), "Bakiyem ne kadar?");

        var tools = model.Calls[0].Options!.Tools!.OfType<AIFunctionDeclaration>().ToList();
        Assert.Equal(["GetBalance", "ListTransactions", "ListCards", "BlockCard", "CreateComplaint"], tools.Select(t => t.Name));
        Assert.All(tools, t => Assert.DoesNotContain("customer", t.JsonSchema.GetRawText(), StringComparison.OrdinalIgnoreCase));

        using var json = JsonDocument.Parse(Assert.Single(model.ToolResultsSentIn(1)));
        var ownAccounts = api.Query(db => db.Accounts.Where(a => a.CustomerId == 3).Select(a => a.Id).ToList());
        Assert.Equal(ownAccounts, json.RootElement.EnumerateArray().Select(a => a.GetProperty("accountId").GetInt32()));
    }

    [Fact]
    public async Task Model_asking_for_another_customers_account_gets_not_found()
    {
        int foreignAccount = api.Query(db => db.Accounts.First(a => a.CustomerId == 2).Id);
        using var model = new ScriptedChatClient(
            ToolCall("c1", "ListTransactions", new() { ["accountId"] = foreignAccount }), Text("tamam"));
        var assistant = CreateAssistant(model);

        await DrainAsync(assistant, await assistant.CreateSessionAsync(Customer, Ct), "2 numaralı hesabın işlemleri?");

        using var json = JsonDocument.Parse(Assert.Single(model.ToolResultsSentIn(1)));
        Assert.Equal("Bu hesap bulunamadı.", json.RootElement.GetProperty("message").GetString());
        Assert.Equal(0, json.RootElement.GetProperty("transactions").GetArrayLength());
    }

    [Fact]
    public async Task Without_a_customer_there_are_no_tools_and_the_model_is_told_why()
    {
        using var model = new ScriptedChatClient(Text("tamam"));
        var assistant = CreateAssistant(model);

        await DrainAsync(assistant, await assistant.CreateSessionAsync(cancellationToken: Ct), "Bakiyem ne kadar?");

        Assert.Empty(model.Calls[0].Options?.Tools ?? []);
        Assert.Contains(AssistantPrompts.NoCustomerNote, model.Calls[0].Options!.Instructions!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Customer_session_without_a_bank_client_is_rejected()
    {
        using var model = new ScriptedChatClient();
        var assistant = new BankAssistant(model, new EmptyRetriever());

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await assistant.CreateSessionAsync(Customer, Ct));
    }

    [Theory]
    [InlineData(-176.11, "-176,11 TL")]
    [InlineData(45379.55, "45.379,55 TL")]
    [InlineData(0, "0,00 TL")]
    public void Amounts_are_formatted_the_turkish_way(decimal amount, string expected) =>
        Assert.Equal(expected, BankingTools.Money(amount, "TRY"));

    [Fact]
    public void Dates_are_shown_in_istanbul_time() =>
        Assert.Equal("23.10.2026 15:43", BankingTools.LocalTime(new DateTime(2026, 10, 23, 12, 43, 0, DateTimeKind.Utc)));

    private BankAssistant CreateAssistant(IChatClient model) =>
        new(model, new EmptyRetriever(), bank: BankingTestHost.CreateClient(api), audit: new InMemoryAuditLog().CreateTrail());

    private async Task<IReadOnlyList<BankTransaction>> LatestSpendingAsync(int customerId, int count)
    {
        var bank = BankingTestHost.CreateClient(api);
        var all = new List<BankTransaction>();
        foreach (var account in await bank.GetAccountsAsync(customerId, Ct))
        {
            all.AddRange((await bank.GetTransactionsAsync(customerId, account.Id, count, TransactionDirection.Spending, Ct))!);
        }

        return [.. all.OrderByDescending(t => t.Date).ThenByDescending(t => t.Id).Take(count)];
    }

    private static async Task DrainAsync(BankAssistant assistant, AssistantSession session, string question)
    {
        var turn = await assistant.AskAsync(session, question, Ct);
        await foreach (string _ in turn.Text)
        {
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class EmptyRetriever : IDocumentRetriever
    {
        public Task<IReadOnlyList<RetrievedChunk>> SearchAsync(
            string query, int top = 5, RetrievalMode mode = RetrievalMode.Hybrid, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RetrievedChunk>>([]);
    }
}
