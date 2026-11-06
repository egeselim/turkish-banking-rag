using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SelimBank.Api.Customers;
using SelimBank.Api.Data;

namespace SelimBank.Api.Tests;

/// <summary>
/// Runs the real Program in memory. Each factory instance gets its own in-memory database, so test classes
/// (one factory per class via IClassFixture) do not see each other's writes.
/// </summary>
public sealed class SelimBankApiFactory : WebApplicationFactory<Program>
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string databaseName = $"selimbank-tests-{Guid.NewGuid():N}";

    public HttpClient CreateClientFor(int customerId)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(CustomerHeader.Name, customerId.ToString(CultureInfo.InvariantCulture));
        return client;
    }

    /// <summary>Reads the database directly, e.g. to compare API output with the stored identifiers.</summary>
    public T Query<T>(Func<SelimBankDbContext, T> query)
    {
        using var scope = Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<SelimBankDbContext>());
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseSetting(
            $"ConnectionStrings:{SelimBankDatabase.ConnectionStringName}",
            $"Data Source={databaseName};Mode=Memory;Cache=Shared");
}

public static class HttpClientJsonExtensions
{
    public static async Task<T> GetJsonAsync<T>(this HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(SelimBankApiFactory.Json, TestContext.Current.CancellationToken))!;
    }
}
