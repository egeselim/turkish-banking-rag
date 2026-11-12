namespace Eval.Runner;

internal static class RetrievalMetrics
{
    public const int TopK = 5;

    public const int CandidateDepth = 20;

    /// <summary>
    /// Hit@1, Hit@5 and MRR over the top 5 (as in step 1.5) on answerable questions only; Hit@20 when any rank
    /// beyond 5 was recorded for the method; latency percentiles on every question, since unanswerable ones are searched too.
    /// </summary>
    public static ModeSummary Summarize(string method, IEnumerable<QuestionResult> questions)
    {
        var list = questions.ToList();
        var ranks = list.Where(q => q.Answerable).Select(q => q.Ranks.GetValueOrDefault(method)).ToList();
        var latencies = list.Where(q => q.LatencyMs.ContainsKey(method)).Select(q => q.LatencyMs[method]).ToList();
        bool deep = list.Any(q => q.Depth(method) > TopK);

        return new ModeSummary(
            method,
            ranks.Count,
            ranks.Count(r => r == 1),
            ranks.Count(r => r is >= 1 and <= TopK),
            deep ? ranks.Count(r => r is >= 1 and <= CandidateDepth) : null,
            ranks.Count == 0 ? 0 : ranks.Sum(r => r is >= 1 and <= TopK ? 1.0 / r.Value : 0) / ranks.Count,
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

    /// <summary>How deep this method was searched for this question: the depth recorded, or 5 for old reports.</summary>
    private static int Depth(this QuestionResult q, string method) =>
        q.Depths?.GetValueOrDefault(method) ?? TopK;
}
