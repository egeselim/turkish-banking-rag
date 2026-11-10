namespace Assistant.Core.Models;

public static class AssistantTelemetry
{
    /// <summary>
    /// Activity source and meter of the model calls (gen_ai spans, token metrics). ServiceDefaults subscribes to
    /// "SelimBank.*", so the name must keep that prefix.
    /// </summary>
    public const string SourceName = "SelimBank.Assistant";
}
