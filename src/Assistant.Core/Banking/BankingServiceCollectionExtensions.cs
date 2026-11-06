using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace Assistant.Core.Banking;

public sealed class SelimBankOptions
{
    public const string SectionName = "SelimBank";

    /// <summary>
    /// Aspire service discovery name of the AppHost resource; resolved to the real address at runtime
    /// ("https+http": prefer https, fall back to http).
    /// </summary>
    public Uri BaseAddress { get; set; } = new("https+http://selimbank-api");
}

public static class BankingServiceCollectionExtensions
{
    public static IServiceCollection AddSelimBankClient(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SelimBankOptions>().Bind(configuration.GetSection(SelimBankOptions.SectionName));

        // Resilience is configured per client (see SelimBank.ServiceDefaults). The API is local and fast, so
        // attempts are short. Unsafe methods are never retried: a repeated POST could open a complaint twice (2.3).
        services.AddHttpClient(SelimBankClient.HttpClientName, (sp, http) =>
                http.BaseAddress = sp.GetRequiredService<IOptions<SelimBankOptions>>().Value.BaseAddress)
            .AddStandardResilienceHandler(o =>
            {
                o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
                o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(15);
                o.Retry.DisableForUnsafeHttpMethods();
            });

        services.AddSingleton<SelimBankClient>();
        return services;
    }
}
