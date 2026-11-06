using SelimBank.Api.Endpoints;

namespace SelimBank.Api.Tests;

// The API never returns a TCKN, full IBAN, full card number, phone or e-mail; they stay in the database.
public class PiiExposureTests(SelimBankApiFactory factory) : IClassFixture<SelimBankApiFactory>
{
    [Fact]
    public async Task No_endpoint_returns_stored_identifiers()
    {
        var secrets = factory.Query(db =>
            db.Customers.ToList().SelectMany(c => new[] { c.Tckn, c.Phone, c.Email })
                .Concat(db.Accounts.Select(a => a.Iban))
                .Concat(db.Cards.Select(c => c.Number))
                .ToList());
        Assert.NotEmpty(secrets);

        using var anonymous = factory.CreateClient();
        var bodies = new List<string> { await GetStringAsync(anonymous, "/customers") };
        foreach (int customerId in factory.Query(db => db.Customers.Select(c => c.Id).ToList()))
        {
            using var client = factory.CreateClientFor(customerId);
            bodies.Add(await GetStringAsync(client, "/accounts"));
            bodies.Add(await GetStringAsync(client, "/cards"));
            foreach (var account in await client.GetJsonAsync<List<AccountDto>>("/accounts"))
            {
                bodies.Add(await GetStringAsync(client, $"/accounts/{account.Id}/transactions?take={BankEndpoints.MaxTake}"));
            }
        }

        foreach (string body in bodies)
        {
            foreach (string secret in secrets)
            {
                Assert.DoesNotContain(secret, body, StringComparison.Ordinal);
            }
        }
    }

    private static Task<string> GetStringAsync(HttpClient client, string path) =>
        client.GetStringAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);
}
