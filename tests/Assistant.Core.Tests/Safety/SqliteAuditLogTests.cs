using Assistant.Core.Audit;

namespace Assistant.Core.Tests.Safety;

public sealed class SqliteAuditLogTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"audit-test-{Guid.NewGuid():N}.db");

    private string ConnectionString => $"Data Source={_path};Pooling=False";

    public void Dispose() => File.Delete(_path);

    [Fact]
    public async Task Rows_are_stored_with_every_field()
    {
        var log = new SqliteAuditLog(ConnectionString);
        var entry = new AuditEntry(
            new DateTimeOffset(2026, 11, 8, 10, 30, 0, TimeSpan.Zero), "abc123", "BlockCard", """{"cardId":"1"}""", AuditOutcome.Succeeded, 42);

        await log.WriteAsync(entry, TestContext.Current.CancellationToken);

        Assert.Equal(entry, Assert.Single(await log.ReadAllAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task Rows_survive_a_restart_and_keep_their_order()
    {
        var ct = TestContext.Current.CancellationToken;
        await new SqliteAuditLog(ConnectionString).WriteAsync(Entry("first"), ct);

        var afterRestart = new SqliteAuditLog(ConnectionString);
        await afterRestart.WriteAsync(Entry("second"), ct);

        Assert.Equal(["first", "second"], (await afterRestart.ReadAllAsync(ct)).Select(e => e.Action));
    }

    private static AuditEntry Entry(string action) =>
        new(DateTimeOffset.UnixEpoch, "p", action, "{}", AuditOutcome.Denied, 0);
}
