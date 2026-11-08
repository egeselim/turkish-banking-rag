using Assistant.Core.Audit;
using Assistant.Privacy;

namespace Assistant.Core.Tests.Safety;

internal sealed class InMemoryAuditLog : IAuditLog
{
    public const string Key = "test-key-not-real";

    private readonly List<AuditEntry> _entries = [];

    public IReadOnlyList<AuditEntry> Entries
    {
        get
        {
            lock (_entries)
            {
                return [.. _entries];
            }
        }
    }

    public AuditTrail CreateTrail() => new(this, new PiiMasker(), new AuditPseudonymizer(Key));

    public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        lock (_entries)
        {
            _entries.Add(entry);
        }

        return Task.CompletedTask;
    }
}
