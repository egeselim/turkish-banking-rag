using System.Net;
using System.Net.Http.Json;
using SelimBank.Api.Data;
using SelimBank.Api.Endpoints;

namespace SelimBank.Api.Tests;

public class TransactionAndComplaintTests(SelimBankApiFactory factory) : IClassFixture<SelimBankApiFactory>
{
    [Fact]
    public async Task Transactions_are_newest_first_and_default_to_ten()
    {
        using var client = factory.CreateClientFor(1);
        int accountId = (await client.GetJsonAsync<List<AccountDto>>("/accounts"))[0].Id;

        var transactions = await client.GetJsonAsync<List<TransactionDto>>($"/accounts/{accountId}/transactions");

        Assert.Equal(BankEndpoints.DefaultTake, transactions.Count);
        Assert.Equal(transactions.OrderByDescending(t => t.Date).Select(t => t.Id), transactions.Select(t => t.Id));
        Assert.All(transactions, t => Assert.Equal(DateTimeKind.Utc, t.Date.Kind));
    }

    [Fact]
    public async Task Take_limits_the_result()
    {
        using var client = factory.CreateClientFor(1);
        int accountId = (await client.GetJsonAsync<List<AccountDto>>("/accounts"))[0].Id;

        var transactions = await client.GetJsonAsync<List<TransactionDto>>($"/accounts/{accountId}/transactions?take=3");

        Assert.Equal(3, transactions.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(BankEndpoints.MaxTake + 1)]
    public async Task Take_out_of_range_returns_400(int take)
    {
        using var client = factory.CreateClientFor(1);
        int accountId = (await client.GetJsonAsync<List<AccountDto>>("/accounts"))[0].Id;

        using var response = await client.GetAsync(
            new Uri($"/accounts/{accountId}/transactions?take={take}", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Card_spending_shows_the_card_last_four_digits()
    {
        using var client = factory.CreateClientFor(1);
        int accountId = (await client.GetJsonAsync<List<AccountDto>>("/accounts"))[0].Id;
        var cards = await client.GetJsonAsync<List<CardDto>>("/cards");

        var transactions = await client.GetJsonAsync<List<TransactionDto>>(
            $"/accounts/{accountId}/transactions?take={BankEndpoints.MaxTake}");

        var byCard = transactions.Where(t => t.CardLast4 is not null).ToList();
        Assert.NotEmpty(byCard);
        Assert.All(byCard, t => Assert.Contains(t.CardLast4, cards.Select(c => c.Last4)));
    }

    [Fact]
    public async Task Complaint_about_own_transaction_is_created_open()
    {
        using var client = factory.CreateClientFor(4);
        int accountId = (await client.GetJsonAsync<List<AccountDto>>("/accounts"))[0].Id;
        int transactionId = (await client.GetJsonAsync<List<TransactionDto>>($"/accounts/{accountId}/transactions?take=1"))[0].Id;

        using var response = await client.PostAsJsonAsync(
            new Uri("/complaints", UriKind.Relative),
            new CreateComplaintRequest("  Bu harcamayı tanımıyorum.  ", transactionId),
            Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var complaint = await response.Content.ReadFromJsonAsync<ComplaintDto>(SelimBankApiFactory.Json, Ct);
        Assert.Equal(ComplaintStatus.Open, complaint!.Status);
        Assert.Equal(transactionId, complaint.TransactionId);
        var stored = factory.Query(db => db.Complaints.Single(c => c.Id == complaint.Id));
        Assert.Equal(4, stored.CustomerId);
        Assert.Equal("Bu harcamayı tanımıyorum.", stored.Description);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task Complaint_without_description_returns_400(string? description)
    {
        using var client = factory.CreateClientFor(4);

        using var response = await client.PostAsJsonAsync(
            new Uri("/complaints", UriKind.Relative), new CreateComplaintRequest(description, null), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Complaint_longer_than_limit_returns_400()
    {
        using var client = factory.CreateClientFor(4);

        using var response = await client.PostAsJsonAsync(
            new Uri("/complaints", UriKind.Relative),
            new CreateComplaintRequest(new string('a', BankEndpoints.MaxComplaintLength + 1), null),
            Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
