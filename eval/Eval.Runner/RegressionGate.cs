using System.Globalization;
using Assistant.Core.Retrieval;

namespace Eval.Runner;

/// <summary>
/// CI gate for the retrieval eval (step 3.5). Offline runs are deterministic (cached embeddings and rerank scores),
/// so any drop of the app's own method below the baseline is a real regression, not noise. A deliberate change
/// passes once the baseline is refreshed with --baseline.
/// </summary>
internal static class RegressionGate
{
    private const double Epsilon = 1e-9;

    /// <summary>The method the app searches with: Rag:Mode, plus the rerank model when one is configured.</summary>
    public static string AppMethod(RagOptions rag) =>
        string.IsNullOrWhiteSpace(rag.RerankModel) ? rag.Mode.ToString() : $"{rag.Mode}+{rag.RerankModel}";

    /// <summary>Failure messages (Turkish, shown in the CI log); empty when the gate passes.</summary>
    public static IReadOnlyList<string> Check(RetrievalReport report, RetrievalReport? baseline, string method, double? minHit5)
    {
        var failures = new List<string>();
        var now = report.Summaries().SingleOrDefault(s => s.Method == method);
        if (now is null)
        {
            return [$"Uygulamanın yöntemi ({method}) raporda yok; --rerank ile o modeli de ölçün."];
        }

        double hit5 = now.Questions == 0 ? 0 : (double)now.Hit5 / now.Questions;
        if (minHit5 is double floor && hit5 < floor)
        {
            failures.Add(string.Create(CultureInfo.InvariantCulture, $"{method} Hit@5 {hit5:P0}, alt sınır {floor:P0}."));
        }

        if (baseline is not null)
        {
            var common = report.Questions.Select(q => q.Id).Intersect(baseline.Questions.Select(q => q.Id)).ToHashSet();
            var before = baseline.Summaries(common).SingleOrDefault(s => s.Method == method);
            if (before is null)
            {
                failures.Add($"Baseline'da {method} yöntemi yok; baseline'ı --baseline ile yenileyin.");
            }
            else
            {
                var current = report.Summaries(common).Single(s => s.Method == method);
                Compare(failures, method, "Hit@1", current.Hit1, before.Hit1);
                Compare(failures, method, "Hit@5", current.Hit5, before.Hit5);
                if (current.Mrr < before.Mrr - Epsilon)
                {
                    failures.Add(string.Create(CultureInfo.InvariantCulture, $"{method} MRR {current.Mrr:F4} < baseline {before.Mrr:F4}."));
                }
            }
        }

        return failures;
    }

    private static void Compare(List<string> failures, string method, string metric, int now, int before)
    {
        if (now < before)
        {
            failures.Add(string.Create(CultureInfo.InvariantCulture, $"{method} {metric} {now} < baseline {before}."));
        }
    }
}
