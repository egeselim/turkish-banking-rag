using System.Text.Json;
using System.Text.Json.Serialization;
using Assistant.Rag.Search;

namespace Eval.Runner;

/// <summary>One question's outcome. Ranks are 1-based positions of the first accepted source in the top 5, or null.</summary>
internal sealed record QuestionResult(
    string Id,
    string Question,
    string? ExpectedDoc,
    IReadOnlyList<string> Tags,
    IReadOnlyDictionary<RetrievalMode, int?> Ranks,
    IReadOnlyDictionary<RetrievalMode, double> LatencyMs,
    IReadOnlyList<string> HybridTop3)
{
    public bool Answerable => ExpectedDoc is not null;
}

internal sealed record ModeSummary(RetrievalMode Mode, int Questions, int Hit1, int Hit5, double Mrr, double P50Ms, double P95Ms);

/// <summary>
/// A retrieval run. Summaries are always recomputed from <see cref="Questions"/>, so a smoke run can be compared
/// with the full baseline on just the questions both contain.
/// </summary>
internal sealed record RetrievalReport(
    int SchemaVersion,
    DateTimeOffset CreatedUtc,
    string Subset,
    string EmbeddingModel,
    int EmbeddingDimensions,
    int ChunkCount,
    int CacheHits,
    int CacheAdded,
    IReadOnlyList<QuestionResult> Questions)
{
    public const int CurrentSchemaVersion = 1;

    public static readonly RetrievalMode[] Modes = [RetrievalMode.VectorOnly, RetrievalMode.KeywordOnly, RetrievalMode.Hybrid];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public IReadOnlyList<ModeSummary> Summaries(IReadOnlySet<string>? onlyIds = null) =>
        [.. Modes.Select(m => RetrievalMetrics.Summarize(m, Questions.Where(q => onlyIds is null || onlyIds.Contains(q.Id))))];

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static RetrievalReport FromJson(string json)
    {
        var report = JsonSerializer.Deserialize<RetrievalReport>(json, Json) ?? throw new InvalidDataException("Empty report.");
        return report.SchemaVersion == CurrentSchemaVersion
            ? report
            : throw new InvalidDataException($"Report schema {report.SchemaVersion} is not {CurrentSchemaVersion}.");
    }
}
