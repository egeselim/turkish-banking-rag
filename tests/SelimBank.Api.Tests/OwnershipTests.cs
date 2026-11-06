using System.Net;
using System.Net.Http.Json;
using SelimBank.Api.Data;
using SelimBank.Api.Endpoints;

namespace SelimBank.Api.Tests;

// Another customer's resource answers exactly like a missing one: 404, never 403, and nothing changes.
public class OwnershipTests(SelimBankApiFactory factory) : IClassFixture<SelimBankApiFactory>
{
    private const int Owner = 1;
    private const int Other = 2;

    [Fact]
    public async Task Transactions_of_another_customers_account_return_404()
    {
        int foreignAccount = factory.Query(db => db.Accounts.First(a => a.CustomerId == Other).Id);
        using var client = factory.CreateClientFor(Owner);

        using var response = await client.GetAsync(Uri($"/accounts/{foreignAccount}/transactions"), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Blocking_another_customers_card_returns_404_and_leaves_it_active()
    {
        int foreignCard = factory.Query(db => db.Cards.First(c => c.CustomerId == Other).Id);
        using var client = factory.CreateClientFor(Owner);

        using var response = await client.PostAsync(Uri($"/cards/{foreignCard}/block"), null, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(CardStatus.Active, factory.Query(db => db.Cards.Single(c => c.Id == foreignCard).Status));
    }

    [Fact]
    public async Task Complaint_about_another_customers_transaction_returns_404_and_is_not_stored()
    {
        int foreignTransaction = factory.Query(db => db.Transactions
            .First(t => db.Accounts.Any(a => a.Id == t.AccountId && a.CustomerId == Other)).Id);
        using var client = factory.CreateClientFor(Owner);

        using var response = await client.PostAsJsonAsync(
            Uri("/complaints"), new CreateComplaintRequest("Bu harcamayı ben yapmadım.", foreignTransaction), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, factory.Query(db => db.Complaints.Count()));
    }

    [Fact]
    public async Task Foreign_and_missing_ids_get_identical_responses()
    {
        int foreignCard = factory.Query(db => db.Cards.First(c => c.CustomerId == Other).Id);
        using var client = factory.CreateClientFor(Owner);

        using var foreign = await client.PostAsync(Uri($"/cards/{foreignCard}/block"), null, Ct);
        using var missing = await client.PostAsync(Uri("/cards/99999/block"), null, Ct);

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(missing.StatusCode, foreign.StatusCode);
        Assert.Equal(await missing.Content.ReadAsStringAsync(Ct), await foreign.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Lists_contain_only_the_callers_records()
    {
        using var client = factory.CreateClientFor(Owner);

        var accounts = await client.GetJsonAsync<List<AccountDto>>("/accounts");
        var cards = await client.GetJsonAsync<List<CardDto>>("/cards");

        Assert.Equal(factory.Query(db => db.Accounts.Where(a => a.CustomerId == Owner).Select(a => a.Id).ToList()),
            accounts.Select(a => a.Id));
        Assert.Equal(factory.Query(db => db.Cards.Where(c => c.CustomerId == Owner).Select(c => c.Id).ToList()),
            cards.Select(c => c.Id));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Uri Uri(string path) => new(path, UriKind.Relative);
}
