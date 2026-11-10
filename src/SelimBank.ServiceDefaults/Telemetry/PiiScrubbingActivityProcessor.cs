using System.Diagnostics;
using Assistant.Privacy;
using OpenTelemetry;

namespace SelimBank.ServiceDefaults.Telemetry;

/// <summary>
/// Safety net before export: masks PII in span names, tags and status descriptions. It is the last line, not the
/// first: telemetry content capture is off (EnableSensitiveData = false) and code never puts user text into spans.
/// Span events (an exception's message, for example) cannot be changed once added; a span whose events or links
/// carry PII is therefore not exported at all (fail closed) and only that fact is counted, never the content.
/// Must be registered before the exporter: processors run in registration order.
/// </summary>
public sealed class PiiScrubbingActivityProcessor(IPiiMasker masker) : BaseProcessor<Activity>
{
    private long _dropped;

    /// <summary>Spans withheld from export because their events or links contained PII.</summary>
    public long DroppedSpans => Interlocked.Read(ref _dropped);

    public override void OnEnd(Activity data)
    {
        ArgumentNullException.ThrowIfNull(data);

        data.DisplayName = masker.Mask(data.DisplayName);

        foreach (var (key, value) in data.TagObjects.ToList())
        {
            if (TelemetryScrubbing.MaskValue(masker, value) is { Changed: true } masked)
            {
                data.SetTag(key, masked.Value);
            }
        }

        if (data.StatusDescription is { } description)
        {
            data.SetStatus(data.Status, masker.Mask(description));
        }

        bool eventsCarryPii = data.Events.Any(e => ContainsPii(e.Name) || e.Tags.Any(t => ContainsPii(t.Value)))
            || data.Links.Any(l => l.Tags?.Any(t => ContainsPii(t.Value)) == true);
        if (eventsCarryPii)
        {
            data.ActivityTraceFlags &= ~ActivityTraceFlags.Recorded;
            Interlocked.Increment(ref _dropped);
        }
    }

    private bool ContainsPii(object? value) => TelemetryScrubbing.MaskValue(masker, value).Changed;
}
