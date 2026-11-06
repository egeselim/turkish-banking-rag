using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace SelimBank.Api.Data;

public static class SelimBankDatabase
{
    public const string ConnectionStringName = "SelimBank";

    public static IServiceCollection AddSelimBankDatabase(this IServiceCollection services) =>
        // The connection string is read when the context is created, so test hosts can override it.
        services.AddDbContext<SelimBankDbContext>((sp, options) =>
            options.UseSqlite(ConnectionString(sp.GetRequiredService<IConfiguration>())));

    /// <summary>
    /// No migrations (decision for 2.1): the schema and seed data are created from scratch on every startup.
    /// The connection string must name a shared in-memory SQLite database (<c>Mode=Memory;Cache=Shared</c>): it starts
    /// empty and exists only while at least one connection is open, so one connection stays open until the app stops.
    /// </summary>
    public static async Task InitializeSelimBankDatabaseAsync(this WebApplication app)
    {
        var keepAlive = new SqliteConnection(ConnectionString(app.Configuration));
        await keepAlive.OpenAsync();
        app.Lifetime.ApplicationStopped.Register(keepAlive.Dispose);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SelimBankDbContext>();
        await db.Database.EnsureCreatedAsync();

        var seed = SeedData.Generate();
        db.AddRange(seed.Customers);
        db.AddRange(seed.Accounts);
        db.AddRange(seed.Cards);
        db.AddRange(seed.Transactions);
        await db.SaveChangesAsync();
    }

    private static string ConnectionString(IConfiguration configuration) =>
        configuration.GetConnectionString(ConnectionStringName)
        ?? throw new InvalidOperationException($"ConnectionStrings:{ConnectionStringName} is not configured.");
}
