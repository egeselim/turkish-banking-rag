using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Assistant.Core.Banking;

/// <summary>
/// HTTP client for SelimBank.Api. The customer id is a method argument supplied by the caller's server-side
/// session and sent as the <c>X-Customer-Id</c> header; the API checks ownership of every record.
/// </summary>
public sealed class SelimBankClient(IHttpClientFactory httpClientFactory)
{
    public const string HttpClientName = "selimbank";
    public const string CustomerHeader = "X-Customer-Id";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Demo customer picker: ids and names only.</summary>
    public async Task<IReadOnlyList<BankCustomer>> GetCustomersAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<List<BankCustomer>>(null, "/customers", cancellationToken).ConfigureAwait(false) ?? [];

    public async Task<IReadOnlyList<BankAccount>> GetAccountsAsync(int customerId, CancellationToken cancellationToken = default) =>
        await GetAsync<List<BankAccount>>(customerId, "/accounts", cancellationToken).ConfigureAwait(false) ?? [];

    /// <returns>Newest first, or <see langword="null"/> if the account does not exist or belongs to someone else.</returns>
    public async Task<IReadOnlyList<BankTransaction>?> GetTransactionsAsync(
        int customerId, int accountId, int take, TransactionDirection direction = TransactionDirection.All,
        CancellationToken cancellationToken = default)
    {
        string path = string.Create(CultureInfo.InvariantCulture, $"/accounts/{accountId}/transactions?take={take}");
        path += direction switch
        {
            TransactionDirection.Spending => "&direction=out",
            TransactionDirection.Income => "&direction=in",
            _ => "",
        };

        return await GetAsync<List<BankTransaction>>(customerId, path, cancellationToken).ConfigureAwait(false);
    }

    /// <returns><see langword="null"/> on 404 (missing or someone else's record).</returns>
    private async Task<T?> GetAsync<T>(int? customerId, string path, CancellationToken cancellationToken)
        where T : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        if (customerId is { } id)
        {
            request.Headers.Add(CustomerHeader, id.ToString(CultureInfo.InvariantCulture));
        }

        var http = httpClientFactory.CreateClient(HttpClientName);
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        // The status code is enough; response bodies are never logged or put into exception messages.
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken).ConfigureAwait(false);
    }
}
