using Assistant.Rag.Search;

namespace Eval.Runner;

internal static class RetrievalMetrics
{
    public const int TopK = 5;

    /// <summary>
    /// Hit@1, Hit@5 and MRR (over the top 5, as in step 1.5) on answerable questions only; latency percentiles
    /// on every question, since unanswerable ones are searched too.
    /// </summary>
    public static ModeSummary Summarize(RetrievalMode mode, IEnumerable<QuestionResult> questions)
    {
        var list = questions.ToList();
        var ranks = list.Where(q => q.Answerable).Select(q => q.Ranks.GetValueOrDefault(mode)).ToList();
        var latencies = list.Where(q => q.LatencyMs.ContainsKey(mode)).Select(q => q.LatencyMs[mode]).ToList();

        return new ModeSummary(
            mode,
            ranks.Count,
            ranks.Count(r => r == 1),
            ranks.Count(r => r is >= 1 and <= TopK),
            ranks.Count == 0 ? 0 : ranks.Sum(r => r is null ? 0 : 1.0 / r.Value) / ranks.Count,
            Percentile(latencies, 0.50),
            Percentile(latencies, 0.95));
    }

    /// <summary>Nearest-rank percentile: the smallest value with at least <paramref name="p"/> of the values at or below it.</summary>
    public static double Percentile(IReadOnlyCollection<double> values, double p)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = values.Order().ToList();
        int index = Math.Max(0, (int)Math.Ceiling(p * sorted.Count) - 1);
        return sorted[index];
    }
}
