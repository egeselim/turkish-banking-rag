using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SelimBank.Api.Customers;
using SelimBank.Api.Data;

namespace SelimBank.Api.Tests;

public class AuthenticationTests(SelimBankApiFactory factory) : IClassFixture<SelimBankApiFactory>
{
    public static TheoryData<string, string> ProtectedEndpoints => new()
    {
        { "GET", "/accounts" },
        { "GET", "/accounts/1/transactions" },
        { "GET", "/cards" },
        { "POST", "/cards/1/block" },
        { "POST", "/complaints" },
    };

    [Theory]
    [MemberData(nameof(ProtectedEndpoints))]
    public async Task Missing_header_returns_401(string method, string path)
    {
        using var client = factory.CreateClient();

        using var response = await SendAsync(client, method, path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("1.0")]
    [InlineData("999")] // well-formed but unknown customer
    [InlineData("1,2")]
    public async Task Invalid_or_unknown_customer_id_returns_401(string headerValue)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(CustomerHeader.Name, headerValue);

        using var response = await client.GetAsync(new Uri("/accounts", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Rejected_block_request_does_not_change_the_card()
    {
        using var client = factory.CreateClient();

        using var response = await SendAsync(client, "POST", "/cards/1/block");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(CardStatus.Active, factory.Query(db => db.Cards.Single(c => c.Id == 1).Status));
    }

    [Fact]
    public async Task Customer_list_needs_no_header_and_returns_only_id_and_name()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/customers", UriKind.Relative), TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var customers = json.RootElement.EnumerateArray().ToList();
        Assert.Equal(SeedData.CustomerCount, customers.Count);
        Assert.All(customers, c => Assert.Equal(["id", "name"], c.EnumerateObject().Select(p => p.Name)));
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string path)
    {
        var uri = new Uri(path, UriKind.Relative);
        var ct = TestContext.Current.CancellationToken;
        return method == "GET"
            ? client.GetAsync(uri, ct)
            : client.PostAsJsonAsync(uri, new { description = "test" }, ct);
    }
}
