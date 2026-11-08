namespace Assistant.Core.Audit;

public enum AuditOutcome
{
    /// <summary>The tool ran and did its job.</summary>
    Succeeded,

    /// <summary>The tool ran, but the bank did not find the card, account or transaction.</summary>
    NotFound,

    /// <summary>The tool threw.</summary>
    Failed,

    /// <summary>The customer pressed Reddet; the tool did not run.</summary>
    Rejected,

    /// <summary>The approval was left unanswered and the conversation moved on; the tool did not run.</summary>
    Cancelled,

    /// <summary>The middleware refused an irreversible tool that had no valid approval; the tool did not run.</summary>
    Denied,

    /// <summary>The input guard stopped the message before the model.</summary>
    InputBlocked,
}

public static class AuditEntryNames
{
    /// <summary><see cref="AuditEntry.Action"/> of rows written by the input guard; tool rows use the tool's name.</summary>
    public const string InputGuard = "InputGuard";
}

/// <param name="At">When the action finished.</param>
/// <param name="CustomerPseudonym">HMAC of the customer id, see <see cref="AuditPseudonymizer"/>.</param>
/// <param name="Action">Tool name, or <see cref="AuditEntryNames.InputGuard"/>.</param>
/// <param name="MaskedArguments">JSON object of arguments after PII masking. Tool results are never stored.</param>
/// <param name="Outcome">What happened.</param>
/// <param name="DurationMs">Time spent in the tool; 0 when it did not run.</param>
public sealed record AuditEntry(
    DateTimeOffset At, string CustomerPseudonym, string Action, string MaskedArguments, AuditOutcome Outcome, long DurationMs);

public interface IAuditLog
{
    Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken);
}
