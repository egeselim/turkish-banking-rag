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
    public static IServiceCollection AddInMemorySelimBank(this IServiceCollection services, SelimBankApiFactory api)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["SelimBank:BaseAddress"] = "http://localhost" })
            .Build();

        services.AddSelimBankClient(configuration);
        services.AddHttpClient(SelimBankClient.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => api.Server.CreateHandler());
        return services;
    }

    public static SelimBankClient CreateClient(SelimBankApiFactory api) =>
        new ServiceCollection().AddInMemorySelimBank(api).BuildServiceProvider().GetRequiredService<SelimBankClient>();
}
