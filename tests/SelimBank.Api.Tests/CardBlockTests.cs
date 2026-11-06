using System.Net.Http.Json;
using SelimBank.Api.Data;
using SelimBank.Api.Endpoints;

namespace SelimBank.Api.Tests;

public class CardBlockTests(SelimBankApiFactory factory) : IClassFixture<SelimBankApiFactory>
{
    [Fact]
    public async Task Blocked_card_shows_as_blocked_in_the_next_query()
    {
        using var client = factory.CreateClientFor(1);
        var card = (await client.GetJsonAsync<List<CardDto>>("/cards"))[0];
        Assert.Equal(CardStatus.Active, card.Status);

        using var response = await client.PostAsync(new Uri($"/cards/{card.Id}/block", UriKind.Relative), null, Ct);
        response.EnsureSuccessStatusCode();
        var blocked = await response.Content.ReadFromJsonAsync<CardDto>(SelimBankApiFactory.Json, Ct);

        Assert.Equal(CardStatus.Blocked, blocked!.Status);
        Assert.NotNull(blocked.BlockedAt);
        var listed = (await client.GetJsonAsync<List<CardDto>>("/cards")).Single(c => c.Id == card.Id);
        Assert.Equal(CardStatus.Blocked, listed.Status);
    }

    [Fact]
    public async Task Blocking_twice_is_idempotent()
    {
        using var client = factory.CreateClientFor(3);
        var card = (await client.GetJsonAsync<List<CardDto>>("/cards"))[0];
        var uri = new Uri($"/cards/{card.Id}/block", UriKind.Relative);

        using var first = await client.PostAsync(uri, null, Ct);
        using var second = await client.PostAsync(uri, null, Ct);

        first.EnsureSuccessStatusCode();
        second.EnsureSuccessStatusCode();
        var a = await first.Content.ReadFromJsonAsync<CardDto>(SelimBankApiFactory.Json, Ct);
        var b = await second.Content.ReadFromJsonAsync<CardDto>(SelimBankApiFactory.Json, Ct);
        Assert.Equal(a, b);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
