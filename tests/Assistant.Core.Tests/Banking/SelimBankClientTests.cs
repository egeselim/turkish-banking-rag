using Assistant.Core.Banking;
using SelimBank.Api.Data;
using SelimBank.Api.Tests;

namespace Assistant.Core.Tests.Banking;

// Against the real API in memory: the header, paths, query strings and JSON contracts must match.
public sealed class SelimBankClientTests(SelimBankApiFactory api) : IClassFixture<SelimBankApiFactory>
{
    private readonly SelimBankClient _client = BankingTestHost.CreateClient(api);

    [Fact]
    public async Task Lists_demo_customers()
    {
        var customers = await _client.GetCustomersAsync(Ct);

        Assert.Equal(SeedData.CustomerCount, customers.Count);
        Assert.All(customers, c => Assert.False(string.IsNullOrWhiteSpace(c.Name)));
    }

    [Fact]
    public async Task Sends_the_customer_id_so_only_own_accounts_come_back()
    {
        var accounts = await _client.GetAccountsAsync(2, Ct);

        Assert.NotEmpty(accounts);
        Assert.Equal(api.Query(db => db.Accounts.Where(a => a.CustomerId == 2).Select(a => a.Id).ToList()), accounts.Select(a => a.Id));
        Assert.All(accounts, a => Assert.Equal(4, a.IbanLast4.Length));
    }

    [Fact]
    public async Task Spending_filter_and_take_reach_the_api()
    {
        int accountId = (await _client.GetAccountsAsync(1, Ct))[0].Id;

        var spending = await _client.GetTransactionsAsync(1, accountId, 3, TransactionDirection.Spending, Ct);

        Assert.NotNull(spending);
        Assert.Equal(3, spending.Count);
        Assert.All(spending, t => Assert.True(t.Amount < 0));
        Assert.Equal(spending.OrderByDescending(t => t.Date).Select(t => t.Id), spending.Select(t => t.Id));
        Assert.All(spending, t => Assert.Equal(DateTimeKind.Utc, t.Date.Kind));
    }

    [Fact]
    public async Task Another_customers_account_returns_null()
    {
        int foreignAccount = api.Query(db => db.Accounts.First(a => a.CustomerId == 2).Id);

        Assert.Null(await _client.GetTransactionsAsync(1, foreignAccount, 5, cancellationToken: Ct));
    }

    [Fact]
    public async Task Unknown_customer_is_rejected_with_401()
    {
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => _client.GetAccountsAsync(999, Ct));

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, error.StatusCode);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
