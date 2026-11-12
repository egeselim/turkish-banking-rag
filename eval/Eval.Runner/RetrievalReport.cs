using System.Text.Json;
using System.Text.Json.Serialization;

namespace Eval.Runner;

/// <summary>
/// One question's outcome per retrieval method ("VectorOnly", "Hybrid", "Hybrid+nvidia/…-rerank-…"). Ranks are the
/// 1-based position of the first accepted source, or null. Base methods are searched 20 deep (Hit@20 = what a
/// reranker could reach); reranked methods 5 deep. Reports from step 3.2 hold ranks up to 5 only.
/// </summary>
internal sealed record QuestionResult(
    string Id,
    string Question,
    string? ExpectedDoc,
    IReadOnlyList<string> Tags,
    IReadOnlyDictionary<string, int?> Ranks,
    IReadOnlyDictionary<string, double> LatencyMs,
    IReadOnlyList<string> HybridTop3,
    IReadOnlyDictionary<string, int>? Depths = null)
{
    public bool Answerable => ExpectedDoc is not null;
}

/// <param name="Hit20">Null when the method was searched only 5 deep.</param>
internal sealed record ModeSummary(string Method, int Questions, int Hit1, int Hit5, int? Hit20, double Mrr, double P50Ms, double P95Ms);

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

    public const string Hybrid = "Hybrid";

    public static readonly string[] BaseMethods = ["VectorOnly", "KeywordOnly", Hybrid];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Methods in the order they first appear (base methods first in reports written by the runner).</summary>
    public IReadOnlyList<string> Methods => [.. Questions.SelectMany(q => q.LatencyMs.Keys).Distinct()];

    public IReadOnlyList<ModeSummary> Summaries(IReadOnlySet<string>? onlyIds = null) =>
        [.. Methods.Select(m => RetrievalMetrics.Summarize(m, Questions.Where(q => onlyIds is null || onlyIds.Contains(q.Id))))];

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static RetrievalReport FromJson(string json)
    {
        var report = JsonSerializer.Deserialize<RetrievalReport>(json, Json) ?? throw new InvalidDataException("Empty report.");
        return report.SchemaVersion == CurrentSchemaVersion
            ? report
            : throw new InvalidDataException($"Report schema {report.SchemaVersion} is not {CurrentSchemaVersion}.");
    }
}
