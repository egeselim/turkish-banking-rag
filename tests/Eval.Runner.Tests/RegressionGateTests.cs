using Assistant.Core.Retrieval;
using Assistant.Rag.Search;

namespace Eval.Runner.Tests;

public sealed class RegressionGateTests
{
    [Fact]
    public void App_method_follows_rag_mode_and_rerank_model()
    {
        Assert.Equal("VectorOnly", RegressionGate.AppMethod(new RagOptions { Mode = RetrievalMode.VectorOnly }));
        Assert.Equal("Hybrid+nvidia/r", RegressionGate.AppMethod(new RagOptions { Mode = RetrievalMode.Hybrid, RerankModel = "nvidia/r" }));
    }

    [Fact]
    public void Same_results_as_the_baseline_pass()
    {
        var report = Report(("a", 1), ("b", 2));

        Assert.Empty(RegressionGate.Check(report, report, "VectorOnly", minHit5: 0.8));
    }

    [Fact]
    public void A_drop_in_hit1_and_mrr_fails_and_names_the_metric()
    {
        var baseline = Report(("a", 1), ("b", 1));
        var report = Report(("a", 1), ("b", 2));

        var failures = RegressionGate.Check(report, baseline, "VectorOnly", minHit5: null);

        Assert.Contains(failures, f => f.Contains("Hit@1 1 < baseline 2", StringComparison.Ordinal));
        Assert.Contains(failures, f => f.Contains("MRR", StringComparison.Ordinal));
        Assert.DoesNotContain(failures, f => f.Contains("Hit@5", StringComparison.Ordinal));
    }

    // Only questions in both reports count: a smoke run is checked against the full baseline's smoke questions.
    [Fact]
    public void Baseline_questions_missing_from_the_run_are_ignored()
    {
        var baseline = Report(("a", 1), ("b", null));
        var report = Report(("a", 1));

        Assert.Empty(RegressionGate.Check(report, baseline, "VectorOnly", minHit5: null));
    }

    [Fact]
    public void An_improvement_passes()
    {
        Assert.Empty(RegressionGate.Check(Report(("a", 1)), Report(("a", 3)), "VectorOnly", minHit5: null));
    }

    [Fact]
    public void Floor_and_missing_method_fail()
    {
        var report = Report(("a", null), ("b", 1));

        Assert.Contains(RegressionGate.Check(report, null, "VectorOnly", minHit5: 0.8), f => f.Contains("alt sınır", StringComparison.Ordinal));
        Assert.Contains(RegressionGate.Check(report, null, "Hybrid+nvidia/r", minHit5: null), f => f.Contains("raporda yok", StringComparison.Ordinal));
    }

    private static RetrievalReport Report(params (string Id, int? Rank)[] questions) => new(
        RetrievalReport.CurrentSchemaVersion, DateTimeOffset.UnixEpoch, "test", "fake", 8, 3, 0, 0,
        [.. questions.Select(q => new QuestionResult(
            q.Id, "soru " + q.Id, "sss", ["fact"],
            new Dictionary<string, int?> { ["VectorOnly"] = q.Rank },
            new Dictionary<string, double> { ["VectorOnly"] = 1 },
            []))]);
}
