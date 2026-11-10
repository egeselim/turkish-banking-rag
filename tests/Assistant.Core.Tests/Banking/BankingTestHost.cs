using Assistant.Core.Banking;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SelimBank.Api.Tests;

namespace Assistant.Core.Tests.Banking;

/// <summary>
/// The real AddSelimBankClient registration (resilience included), with requests routed to an in-memory
/// SelimBank.Api instead of Aspire service discovery.
/// </summary>
internal static class BankingTestHost
{
    public static IServiceCollection AddInMemorySelimBank(
        this IServiceCollection services, SelimBankApiFactory api, RequestCounter? counter = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["SelimBank:BaseAddress"] = "http://localhost" })
            .Build();

        services.AddSelimBankClient(configuration);
        services.AddHttpClient(SelimBankClient.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new CountingHandler(counter ?? new RequestCounter())
            {
                InnerHandler = api.Server.CreateHandler(),
            });
        return services;
    }

    public static SelimBankClient CreateClient(SelimBankApiFactory api, RequestCounter? counter = null) =>
        new ServiceCollection().AddInMemorySelimBank(api, counter).BuildServiceProvider().GetRequiredService<SelimBankClient>();
}

/// <summary>Counts write requests that actually reach the bank API (2.3 acceptance: 0, 0, 1).</summary>
internal sealed class RequestCounter
{
    private int _posts;

    public int Posts => Volatile.Read(ref _posts);

    /// <summary>Every request that reached the API: method, path and the customer header it carried.</summary>
    public System.Collections.Concurrent.ConcurrentQueue<(string Method, string Path, string? Customer)> Requests { get; } = new();

    public void CountPost() => Interlocked.Increment(ref _posts);
}

internal sealed class CountingHandler(RequestCounter counter) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post)
        {
            counter.CountPost();
        }

        string? customer = request.Headers.TryGetValues(SelimBank.Api.Customers.CustomerHeader.Name, out var values)
            ? string.Join(",", values)
            : null;
        counter.Requests.Enqueue((request.Method.Method, request.RequestUri?.AbsolutePath ?? "", customer));

        return base.SendAsync(request, cancellationToken);
    }
}
