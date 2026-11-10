using Assistant.Privacy;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace SelimBank.ServiceDefaults.Telemetry;

/// <summary>
/// Safety net before export: masks PII in log messages, attributes and exceptions sent to OpenTelemetry (the
/// dashboard). The console logger is not covered; code keeps user text out of log templates in the first place.
/// Scopes cannot be changed here; ours carry request metadata only.
/// Must be registered before the exporter: processors run in registration order.
/// </summary>
public sealed class PiiScrubbingLogProcessor(IPiiMasker masker) : BaseProcessor<LogRecord>
{
    public override void OnEnd(LogRecord data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (data.FormattedMessage is { } formatted)
        {
            data.FormattedMessage = masker.Mask(formatted);
        }

        if (data.Body is { } body)
        {
            data.Body = masker.Mask(body);
        }

        if (data.Attributes is { Count: > 0 } attributes)
        {
            data.Attributes = [.. attributes.Select(a => new KeyValuePair<string, object?>(a.Key, TelemetryScrubbing.MaskValue(masker, a.Value).Value))];
        }

        if (data.Exception is { } exception and not ScrubbedException)
        {
            data.Exception = new ScrubbedException(exception, masker);
        }
    }
}

/// <summary>
/// Stands in for a logged exception: the original type name, message and stack trace (inner exceptions included),
/// all masked. Exporters read <see cref="Exception.Message"/> and <see cref="ToString"/>.
/// </summary>
public sealed class ScrubbedException : Exception
{
    private readonly string _details;

    public ScrubbedException(Exception original, IPiiMasker masker)
        : base(masker.Mask($"{original.GetType().FullName}: {original.Message}"))
    {
        ArgumentNullException.ThrowIfNull(original);
        _details = masker.Mask(original.ToString());
    }

    public override string? StackTrace => _details;

    public override string ToString() => _details;
}
