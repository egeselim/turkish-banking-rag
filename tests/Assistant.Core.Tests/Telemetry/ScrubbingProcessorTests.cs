using System.Diagnostics;
using Assistant.Privacy;
using Assistant.Privacy.Tests.TestData;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Trace;
using SelimBank.ServiceDefaults.Telemetry;

namespace Assistant.Core.Tests.Telemetry;

/// <summary>The scrubbers with a capturing exporter registered after them, as in ServiceDefaults.</summary>
public sealed class ScrubbingProcessorTests
{
    private static readonly string Tckn = FakePii.Tckn(FakePii.Seeded(31));
    private static readonly string[] ExpectedList = ["ok", "[TCKN]"];

    [Fact]
    public void Span_name_tags_and_status_are_masked()
    {
        var (spans, _) = RecordSpan(a =>
        {
            a.DisplayName = $"ara {Tckn}";
            a.SetTag("text", $"kimlik {Tckn}");
            a.SetTag("list", new[] { "ok", Tckn });
            a.SetTag("count", 3);
            a.SetStatus(ActivityStatusCode.Error, $"hata {Tckn}");
        });

        var span = Assert.Single(spans);
        Assert.Equal("ara [TCKN]", span.DisplayName);
        Assert.Equal("kimlik [TCKN]", span.GetTagItem("text"));
        Assert.Equal(ExpectedList, span.GetTagItem("list"));
        Assert.Equal(3, span.GetTagItem("count"));
        Assert.Equal("hata [TCKN]", span.StatusDescription);
    }

    [Fact]
    public void Span_whose_event_carries_pii_is_not_exported()
    {
        var (spans, processor) = RecordSpan(a => a.AddException(new InvalidOperationException($"kimlik {Tckn}")));

        Assert.Empty(spans);
        Assert.Equal(1, processor.DroppedSpans);
    }

    [Fact]
    public void Span_with_harmless_events_is_exported_unchanged()
    {
        var (spans, processor) = RecordSpan(a => a.AddEvent(new ActivityEvent("retry", tags: new ActivityTagsCollection { ["attempt"] = 2 })));

        Assert.Single(spans);
        Assert.Equal(0, processor.DroppedSpans);
    }

    [Fact]
    public void Log_message_attributes_and_exception_are_masked()
    {
        var records = new List<(string? Message, string Attributes, string? Exception)>();
        using (var factory = LoggerFactory.Create(b => b.AddOpenTelemetry(o =>
               {
                   o.IncludeFormattedMessage = true;
                   o.AddProcessor(new PiiScrubbingLogProcessor(new PiiMasker()));
                   o.AddProcessor(new SimpleLogRecordExportProcessor(new Capture<LogRecord>(r => records.Add((
                       r.FormattedMessage,
                       string.Join(";", r.Attributes?.Select(a => $"{a.Key}={a.Value}") ?? []),
                       r.Exception?.ToString())))));
               })))
        {
#pragma warning disable CA1848, CA2254 // The test needs a template with the value inside.
            factory.CreateLogger("test").LogError(new InvalidOperationException($"bozuk {Tckn}"), "Müşteri {Value}", Tckn);
#pragma warning restore CA1848, CA2254
        }

        var (message, attributes, exception) = Assert.Single(records);
        Assert.Equal("Müşteri [TCKN]", message);
        Assert.DoesNotContain(Tckn, attributes, StringComparison.Ordinal);
        Assert.Contains("Value=[TCKN]", attributes, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException: bozuk [TCKN]", exception, StringComparison.Ordinal);
        Assert.DoesNotContain(Tckn, exception, StringComparison.Ordinal);
    }

    private static (List<Activity> Spans, PiiScrubbingActivityProcessor Processor) RecordSpan(Action<Activity> fill)
    {
        string sourceName = $"scrub-test-{Guid.NewGuid():N}";
        var spans = new List<Activity>();
        var processor = new PiiScrubbingActivityProcessor(new PiiMasker());
        using (var provider = Sdk.CreateTracerProviderBuilder()
                   .AddSource(sourceName)
                   .AddProcessor(processor)
                   .AddProcessor(new SimpleActivityExportProcessor(new Capture<Activity>(spans.Add)))
                   .Build())
        using (var source = new ActivitySource(sourceName))
        {
            using var activity = source.StartActivity("span")!;
            fill(activity);
        }

        return (spans, processor);
    }

    private sealed class Capture<T>(Action<T> add) : BaseExporter<T>
        where T : class
    {
        public override ExportResult Export(in Batch<T> batch)
        {
            foreach (var item in batch)
            {
                add(item);
            }

            return ExportResult.Success;
        }
    }
}
