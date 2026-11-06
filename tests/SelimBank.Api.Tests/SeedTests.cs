using System.Text.Json;
using Assistant.Privacy.Validation;
using SelimBank.Api.Data;
using SelimBank.Api.Endpoints;

namespace SelimBank.Api.Tests;

public class SeedTests(SelimBankApiFactory factory) : IClassFixture<SelimBankApiFactory>
{
    [Fact]
    public void Same_seed_generates_identical_data()
    {
        string first = JsonSerializer.Serialize(SeedData.Generate());
        string second = JsonSerializer.Serialize(SeedData.Generate());

        Assert.Equal(first, second);
    }

    [Fact]
    public void Different_seed_generates_different_data()
    {
        Assert.NotEqual(
            JsonSerializer.Serialize(SeedData.Generate(1)),
            JsonSerializer.Serialize(SeedData.Generate(2)));
    }

    [Fact]
    public async Task Two_startups_serve_identical_data()
    {
        // `factory` is one startup; a second, independent host with its own database is the other.
        await using var second = new SelimBankApiFactory();

        for (int customerId = 1; customerId <= SeedData.CustomerCount; customerId++)
        {
            using var a = factory.CreateClientFor(customerId);
            using var b = second.CreateClientFor(customerId);
            var accounts = await a.GetJsonAsync<List<AccountDto>>("/accounts");

            Assert.Equivalent(accounts, await b.GetJsonAsync<List<AccountDto>>("/accounts"), strict: true);
            Assert.Equivalent(
                await a.GetJsonAsync<List<CardDto>>("/cards"),
                await b.GetJsonAsync<List<CardDto>>("/cards"),
                strict: true);
            Assert.Equivalent(
                await a.GetJsonAsync<List<TransactionDto>>($"/accounts/{accounts[0].Id}/transactions?take=100"),
                await b.GetJsonAsync<List<TransactionDto>>($"/accounts/{accounts[0].Id}/transactions?take=100"),
                strict: true);
        }
    }

    [Fact]
    public void Stored_identifiers_pass_privacy_checksums()
    {
        var (tckns, ibans, cardNumbers) = factory.Query(db => (
            db.Customers.Select(c => c.Tckn).ToList(),
            db.Accounts.Select(a => a.Iban).ToList(),
            db.Cards.Select(c => c.Number).ToList()));

        Assert.Equal(SeedData.CustomerCount, tckns.Count);
        Assert.All(tckns, t => Assert.True(Checksums.IsValidTckn(t), t));
        Assert.All(ibans, i =>
        {
            Assert.Equal(26, i.Length);
            Assert.StartsWith("TR", i, StringComparison.Ordinal);
            Assert.True(Checksums.IsValidIban(i), i);
        });
        Assert.All(cardNumbers, n =>
        {
            Assert.Equal(16, n.Length);
            Assert.True(Checksums.IsValidLuhn(n), n);
        });
    }

    [Fact]
    public void Every_customer_has_the_planned_shape()
    {
        var seed = SeedData.Generate();

        Assert.Equal(SeedData.CustomerCount, seed.Customers.Count);
        Assert.All(seed.Customers, c =>
        {
            int accounts = seed.Accounts.Count(a => a.CustomerId == c.Id);
            int cards = seed.Cards.Count(k => k.CustomerId == c.Id);
            var accountIds = seed.Accounts.Where(a => a.CustomerId == c.Id).Select(a => a.Id).ToHashSet();
            int transactions = seed.Transactions.Count(t => accountIds.Contains(t.AccountId));

            Assert.InRange(accounts, 1, 2);
            Assert.InRange(cards, 1, 2);
            Assert.True(transactions >= 20, $"customer {c.Id} has {transactions} transactions");
        });
        Assert.All(seed.Accounts, a => Assert.True(a.Balance > 0, $"account {a.Id} balance {a.Balance}"));
        Assert.Equal(seed.Transactions.Count, seed.Transactions.Select(t => t.Id).Distinct().Count());
    }
}
