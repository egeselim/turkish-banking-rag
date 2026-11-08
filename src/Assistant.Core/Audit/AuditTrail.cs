using System.Text.Encodings.Web;
using System.Text.Json;
using Assistant.Privacy;
using Microsoft.Extensions.Logging;

namespace Assistant.Core.Audit;

/// <summary>
/// The only way rows get written: it masks the arguments and pseudonymizes the customer itself, so a caller
/// cannot store raw values by accident.
/// </summary>
public sealed class AuditTrail(IAuditLog log, IPiiMasker masker, AuditPseudonymizer pseudonymizer, TimeProvider? time = null, ILogger? logger = null)
{
    // Default JSON escaping turns '+' into +; masked text must stay readable and must not be re-encoded.
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>
    /// Writes one row. A storage failure is logged (without any of the row's data) and swallowed: the bank action
    /// has already happened by now, and reporting it to the model as failed would be false.
    /// The write ignores cancellation for the same reason.
    /// </summary>
    public async Task RecordAsync(
        int? customerId, string action, IEnumerable<KeyValuePair<string, object?>>? arguments, AuditOutcome outcome, TimeSpan duration = default)
    {
        var entry = new AuditEntry(
            _time.GetUtcNow(),
            pseudonymizer.Pseudonymize(customerId),
            action,
            MaskArguments(arguments),
            outcome,
            (long)duration.TotalMilliseconds);

        try
        {
            await log.WriteAsync(entry, CancellationToken.None).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Any storage failure must leave the finished action's result intact.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            if (logger is not null)
            {
                AuditLogMessages.WriteFailed(logger, action, outcome, ex.GetType().Name);
            }
        }
    }

    // Each value is masked on its own, before JSON encoding: encoded text could hide a number from the detector.
    private string MaskArguments(IEnumerable<KeyValuePair<string, object?>>? arguments)
    {
        var masked = new Dictionary<string, string?>();
        foreach (var (name, value) in arguments ?? [])
        {
            masked[masker.Mask(name)] = value is null ? null : masker.Mask(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "");
        }

        return JsonSerializer.Serialize(masked, Json);
    }
}
