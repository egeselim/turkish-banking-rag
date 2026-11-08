using System.Text.Json;
using Assistant.Core.Audit;
using Assistant.Privacy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Assistant.Core.Tests.Safety;

public sealed class AuditTrailTests
{
    [Fact]
    public async Task Provider_arguments_are_masked_one_by_one_before_encoding()
    {
        var log = new InMemoryAuditLog();
        string tckn = SyntheticPii.NationalId();
        Dictionary<string, object?> arguments = new()
        {
            ["description"] = JsonSerializer.SerializeToElement($"Kimlik {tckn}, telefon +90 532 123 45 67"),
            ["cardId"] = JsonSerializer.SerializeToElement(7),
            ["transactionId"] = null,
        };

        await log.CreateTrail().RecordAsync(1, "Tool", arguments, AuditOutcome.Succeeded);

        using var stored = JsonDocument.Parse(Assert.Single(log.Entries).MaskedArguments);
        Assert.Equal("Kimlik [TCKN], telefon [TELEFON]", stored.RootElement.GetProperty("description").GetString());
        Assert.Equal("7", stored.RootElement.GetProperty("cardId").GetString());
        Assert.Equal(JsonValueKind.Null, stored.RootElement.GetProperty("transactionId").ValueKind);
    }

    [Fact]
    public async Task Storage_failure_does_not_change_the_finished_action()
    {
        var trail = new AuditTrail(new BrokenLog(), new PiiMasker(), new AuditPseudonymizer(InMemoryAuditLog.Key));

        await trail.RecordAsync(1, "BlockCard", null, AuditOutcome.Succeeded); // must not throw
    }

    [Fact]
    public async Task Di_composition_writes_to_the_configured_sqlite_file()
    {
        string path = Path.Combine(Path.GetTempPath(), $"audit-di-test-{Guid.NewGuid():N}.db");
        string connectionString = $"Data Source={path};Pooling=False";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Audit:ConnectionString"] = connectionString })
            .Build();
        try
        {
            using var services = new ServiceCollection().AddAuditLog(configuration).BuildServiceProvider();

            await services.GetRequiredService<AuditTrail>().RecordAsync(1, "BlockCard", null, AuditOutcome.Denied);

            var row = Assert.Single(await new SqliteAuditLog(connectionString).ReadAllAsync(TestContext.Current.CancellationToken));
            Assert.Equal(("BlockCard", AuditOutcome.Denied), (row.Action, row.Outcome));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class BrokenLog : IAuditLog
    {
        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("disk full");
    }
}
