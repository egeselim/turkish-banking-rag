using System.Text.Json;
using System.Text.Json.Serialization;

namespace Eval.Runner;

/// <summary>
/// One answered question. Judge scores are 1–5 (null when the judge was not run or failed: unanswerable questions
/// are not judged, only checked for a refusal). <see cref="Cited"/> lists "doc:label" of the sources the answer cites.
/// </summary>
internal sealed record AnswerResult(
    string Id,
    string Question,
    string? ExpectedDoc,
    IReadOnlyList<string> Tags,
    string Answer,
    IReadOnlyList<string> Cited,
    bool CitationCorrect,
    bool Refused,
    IReadOnlyDictionary<string, double?> Scores,
    IReadOnlyDictionary<string, string> Reasons,
    double LatencyMs,
    TokenUsage AssistantTokens,
    TokenUsage JudgeTokens,
    string? Error)
{
    public bool Answerable => ExpectedDoc is not null;
}

internal sealed record Pricing(double InputPerMillion, double OutputPerMillion)
{
    public double Cost(TokenUsage usage) => (usage.Input * InputPerMillion + usage.Output * OutputPerMillion) / 1_000_000;
}

internal sealed record AnswerSummary(
    int Answerable,
    int Unanswerable,
    IReadOnlyDictionary<string, double?> MeanScores,
    IReadOnlyDictionary<string, double?> ShareAtLeast4,
    double CitationRate,
    double CitationAccuracy,
    double FalseRefusalRate,
    double CorrectRefusalRate,
    int Errors,
    double P50Ms,
    double P95Ms,
    TokenUsage AssistantTokens,
    TokenUsage JudgeTokens,
    IReadOnlyList<string> InventedPhoneIds);

internal sealed record AnswerReport(
    int SchemaVersion,
    DateTimeOffset CreatedUtc,
    string Subset,
    string ChatModel,
    string JudgeModel,
    string RetrievalMode,
    Pricing? Pricing,
    IReadOnlyList<AnswerResult> Questions,
    IReadOnlyList<string>? SourcePhones = null)
{
    public const int CurrentSchemaVersion = 1;

    public static readonly string[] Metrics =
    [
        Microsoft.Extensions.AI.Evaluation.Quality.GroundednessEvaluator.GroundednessMetricName,
        Microsoft.Extensions.AI.Evaluation.Quality.CompletenessEvaluator.CompletenessMetricName,
        Microsoft.Extensions.AI.Evaluation.Quality.RelevanceEvaluator.RelevanceMetricName,
    ];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // Turkish answers stay readable in the diff
    };

    /// <summary>
    /// Rates over answerable questions (citation, false refusal) or unanswerable ones (correct refusal); score
    /// means over the questions the judge scored. <paramref name="onlyIds"/> limits it to questions shared with a baseline.
    /// </summary>
    public AnswerSummary Summarize(IReadOnlySet<string>? onlyIds = null)
    {
        var all = Questions.Where(q => onlyIds is null || onlyIds.Contains(q.Id)).ToList();
        var answerable = all.Where(q => q.Answerable).ToList();
        var unanswerable = all.Where(q => !q.Answerable).ToList();

        double? Mean(string metric)
        {
            var values = answerable.Select(q => q.Scores.GetValueOrDefault(metric)).Where(v => v is not null).Select(v => v!.Value).ToList();
            return values.Count == 0 ? null : values.Average();
        }

        double? Share(string metric)
        {
            var values = answerable.Select(q => q.Scores.GetValueOrDefault(metric)).Where(v => v is not null).ToList();
            return values.Count == 0 ? null : (double)values.Count(v => v >= 4) / values.Count;
        }

        var cited = answerable.Where(q => q.Cited.Count > 0).ToList();
        var latencies = all.Select(q => q.LatencyMs).ToList();

        return new AnswerSummary(
            answerable.Count,
            unanswerable.Count,
            Metrics.ToDictionary(m => m, Mean),
            Metrics.ToDictionary(m => m, Share),
            Rate(cited.Count, answerable.Count),
            Rate(cited.Count(q => q.CitationCorrect), cited.Count),
            Rate(answerable.Count(q => q.Refused), answerable.Count),
            Rate(unanswerable.Count(q => q.Refused), unanswerable.Count),
            all.Count(q => q.Error is not null),
            RetrievalMetrics.Percentile(latencies, 0.50),
            RetrievalMetrics.Percentile(latencies, 0.95),
            all.Aggregate(TokenUsage.None, (sum, q) => sum.Add(q.AssistantTokens)),
            all.Aggregate(TokenUsage.None, (sum, q) => sum.Add(q.JudgeTokens)),
            InventedPhoneIds(all));
    }

    /// <summary>
    /// Questions whose answer contains a phone number that is not in the sources. Computed from the stored answer
    /// text, so it also works for reports written before the check existed (their sources had no phone numbers).
    /// </summary>
    private List<string> InventedPhoneIds(IEnumerable<AnswerResult> questions)
    {
        var known = (SourcePhones ?? []).ToHashSet(StringComparer.Ordinal);
        return [.. questions.Where(q => ContactCheck.InventedPhones(q.Answer, known).Count > 0).Select(q => q.Id)];
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static AnswerReport FromJson(string json)
    {
        var report = JsonSerializer.Deserialize<AnswerReport>(json, Json) ?? throw new InvalidDataException("Empty report.");
        return report.SchemaVersion == CurrentSchemaVersion
            ? report
            : throw new InvalidDataException($"Report schema {report.SchemaVersion} is not {CurrentSchemaVersion}.");
    }

    private static double Rate(int count, int total) => total == 0 ? 0 : (double)count / total;
}
