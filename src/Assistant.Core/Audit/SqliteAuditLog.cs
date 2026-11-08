using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Assistant.Core.Audit;

/// <summary>
/// Audit rows in the assistant's own SQLite file. It is separate from the bank's database on purpose: the bank
/// API is a different service whose data is recreated on every start, while this trail must survive restarts.
/// </summary>
public sealed class SqliteAuditLog(string connectionString) : IAuditLog
{
    private const string CreateTable = """
        CREATE TABLE IF NOT EXISTS AuditEntries (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            At TEXT NOT NULL,
            CustomerPseudonym TEXT NOT NULL,
            Action TEXT NOT NULL,
            MaskedArguments TEXT NOT NULL,
            Outcome TEXT NOT NULL,
            DurationMs INTEGER NOT NULL
        );
        """;

    public async Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO AuditEntries (At, CustomerPseudonym, Action, MaskedArguments, Outcome, DurationMs)
            VALUES ($at, $customer, $action, $arguments, $outcome, $duration);
            """;
        command.Parameters.AddWithValue("$at", entry.At.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$customer", entry.CustomerPseudonym);
        command.Parameters.AddWithValue("$action", entry.Action);
        command.Parameters.AddWithValue("$arguments", entry.MaskedArguments);
        command.Parameters.AddWithValue("$outcome", entry.Outcome.ToString());
        command.Parameters.AddWithValue("$duration", entry.DurationMs);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AuditEntry>> ReadAllAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT At, CustomerPseudonym, Action, MaskedArguments, Outcome, DurationMs FROM AuditEntries ORDER BY Id;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var entries = new List<AuditEntry>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            entries.Add(new AuditEntry(
                DateTimeOffset.Parse(reader.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                Enum.Parse<AuditOutcome>(reader.GetString(4)),
                reader.GetInt64(5)));
        }

        return entries;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = CreateTable;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }
}
