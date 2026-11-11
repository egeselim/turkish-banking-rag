using Assistant.Rag.Search;

namespace Eval.Runner.Tests;

public sealed class ReportTests
{
    [Fact]
    public void Metrics_follow_hand_computed_values()
    {
        // Hybrid ranks 1, 2, miss, 5 → Hit@1 1, Hit@5 3, MRR (1 + 1/2 + 0 + 1/5) / 4 = 0.425. The unanswerable
        // question only counts for latency: 10, 20, 30, 40, 50 ms → p50 30, p95 50.
        var report = Report(Q("a", 1, 10), Q("b", 2, 20), Q("c", null, 30), Q("d", 5, 40), Unanswerable("u", 50));

        var hybrid = report.Summaries().Single(s => s.Mode == RetrievalMode.Hybrid);

        Assert.Equal(4, hybrid.Questions);
        Assert.Equal(1, hybrid.Hit1);
        Assert.Equal(3, hybrid.Hit5);
        Assert.Equal(0.425, hybrid.Mrr, 10);
        Assert.Equal(30, hybrid.P50Ms);
        Assert.Equal(50, hybrid.P95Ms);
    }

    [Theory]
    [InlineData(new double[] { 5 }, 0.5, 5)]
    [InlineData(new double[] { 4, 1, 3, 2 }, 0.5, 2)]
    [InlineData(new double[] { 4, 1, 3, 2 }, 0.95, 4)]
    [InlineData(new double[0], 0.5, 0)]
    public void Percentile_is_nearest_rank(double[] values, double p, double expected)
    {
        Assert.Equal(expected, RetrievalMetrics.Percentile(values, p));
    }

    [Fact]
    public void Json_round_trip_keeps_ranks_and_nulls()
    {
        var report = Report(Q("a", 1, 10), Q("b", null, 20), Unanswerable("u", 5));

        var back = RetrievalReport.FromJson(report.ToJson());

        Assert.Equal(1, back.Questions[0].Ranks[RetrievalMode.Hybrid]);
        Assert.Null(back.Questions[1].Ranks[RetrievalMode.Hybrid]);
        Assert.Empty(back.Questions[2].Ranks);
        Assert.Equal(report.Summaries(), back.Summaries());
    }

    [Fact]
    public void Unknown_schema_version_is_rejected()
    {
        string json = Report(Q("a", 1, 1)).ToJson().Replace("\"schema_version\": 1", "\"schema_version\": 99", StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => RetrievalReport.FromJson(json));
    }

    // A smoke run is compared with the full baseline on the questions both contain, not on the baseline's totals.
    [Fact]
    public void Comparison_uses_only_common_questions_and_lists_changed_ranks()
    {
        var baseline = Report(Q("a", 1, 10), Q("b", 1, 10), Q("c", null, 10));
        var current = Report(Q("a", 1, 10), Q("b", 3, 10));

        string markdown = MarkdownReport.Render(current, baseline, "baseline-retrieval.json");

        Assert.Contains("| Hybrid | 1/2 (-1) | 2/2 (=) |", markdown, StringComparison.Ordinal);
        Assert.Contains("| b: soru b | 1 | 3 |", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("| a: soru a | 1 | 1 |", markdown, StringComparison.Ordinal);
        Assert.Contains("ortak 2 soru", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Markdown_without_baseline_lists_misses_and_unanswerable_top_results()
    {
        string markdown = MarkdownReport.Render(Report(Q("a", null, 10), Unanswerable("u", 5)), baseline: null, baselineName: null);

        Assert.Contains("Karşılaştırma: yok", markdown, StringComparison.Ordinal);
        Assert.Contains("- a: soru a → sss:Soru 1", markdown, StringComparison.Ordinal);
        Assert.Contains("- u: soru u → sss:Soru 1", markdown, StringComparison.Ordinal);
    }

    private static RetrievalReport Report(params QuestionResult[] questions) =>
        new(RetrievalReport.CurrentSchemaVersion, DateTimeOffset.UnixEpoch, "test", "fake", 8, 3, 0, 0, questions);

    private static QuestionResult Q(string id, int? hybridRank, double ms) => new(
        id,
        "soru " + id,
        "sss",
        ["fact"],
        new Dictionary<RetrievalMode, int?> { [RetrievalMode.VectorOnly] = hybridRank, [RetrievalMode.KeywordOnly] = null, [RetrievalMode.Hybrid] = hybridRank },
        RetrievalReport.Modes.ToDictionary(m => m, _ => ms),
        ["sss:Soru 1"]);

    private static QuestionResult Unanswerable(string id, double ms) => new(
        id,
        "soru " + id,
        null,
        ["unanswerable"],
        new Dictionary<RetrievalMode, int?>(),
        RetrievalReport.Modes.ToDictionary(m => m, _ => ms),
        ["sss:Soru 1"]);
}
