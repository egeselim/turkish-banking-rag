using Assistant.Core.Audit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Assistant.Core.Tests.Safety;

/// <summary>User decision (2.4): HMAC pseudonyms; a missing key means a random key and a warning, not a failure.</summary>
public sealed class AuditPseudonymizerTests
{
    private const string Key = "test-key-not-real";

    [Fact]
    public void Same_key_gives_a_stable_pseudonym_across_instances()
    {
        Assert.Equal(new AuditPseudonymizer(Key).Pseudonymize(1), new AuditPseudonymizer(Key).Pseudonymize(1));
    }

    [Fact]
    public void Different_customers_get_different_pseudonyms()
    {
        var pseudonymizer = new AuditPseudonymizer(Key);

        var all = Enumerable.Range(1, 5).Select(id => pseudonymizer.Pseudonymize(id)).ToList();

        Assert.Equal(5, all.Distinct().Count());
    }

    [Fact]
    public void Pseudonym_depends_on_the_key_so_small_ids_cannot_be_guessed_without_it()
    {
        Assert.NotEqual(new AuditPseudonymizer(Key).Pseudonymize(1), new AuditPseudonymizer("another-test-key").Pseudonymize(1));
    }

    [Fact]
    public void Pseudonym_is_a_short_hex_value_that_does_not_show_the_id()
    {
        string pseudonym = new AuditPseudonymizer(Key).Pseudonymize(12345);

        Assert.Matches("^[0-9a-f]{16}$", pseudonym);
        Assert.DoesNotContain("12345", pseudonym, StringComparison.Ordinal);
    }

    [Fact]
    public void No_customer_has_a_fixed_label()
    {
        Assert.Equal(AuditPseudonymizer.NoCustomer, new AuditPseudonymizer(Key).Pseudonymize(null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_key_warns_and_uses_a_random_key(string? key)
    {
        var logger = new CapturingLogger();

        var first = new AuditPseudonymizer(key, logger);
        var second = new AuditPseudonymizer(key);

        Assert.NotEqual(first.Pseudonymize(1), second.Pseudonymize(1)); // random per instance
        Assert.Equal(first.Pseudonymize(1), first.Pseudonymize(1));     // stable within one run
        var warning = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains("Audit:PseudonymKey", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Configured_key_logs_nothing()
    {
        var logger = new CapturingLogger();

        _ = new AuditPseudonymizer(Key, logger);

        Assert.Empty(logger.Entries);
    }

    [Fact]
    public void Di_reads_the_key_from_configuration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Audit:PseudonymKey"] = Key })
            .Build();
        using var services = new ServiceCollection().AddAuditLog(configuration).BuildServiceProvider();

        string pseudonym = services.GetRequiredService<AuditPseudonymizer>().Pseudonymize(1);

        Assert.Equal(new AuditPseudonymizer(Key).Pseudonymize(1), pseudonym);
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
