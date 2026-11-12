using System.Globalization;
using System.Text;

namespace Eval.Runner;

/// <summary>Turkish Markdown view of a <see cref="RetrievalReport"/>, optionally against a baseline.</summary>
internal static class MarkdownReport
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>"Hybrid+nvidia/nemotron-3.5-rerank-8b" → "Hybrid + rerank (nemotron-3.5-rerank-8b)".</summary>
    public static string MethodName(string method)
    {
        string[] parts = method.Split('+', 2);
        string mode = parts[0] switch
        {
            "VectorOnly" => "Vektör",
            "KeywordOnly" => "BM25",
            _ => parts[0],
        };
        return parts.Length == 1 ? mode : $"{mode} + rerank ({parts[1][(parts[1].LastIndexOf('/') + 1)..]})";
    }

    public static string Render(RetrievalReport report, RetrievalReport? baseline, string? baselineName)
    {
        var sb = new StringBuilder();
        var answerable = report.Questions.Where(q => q.Answerable).ToList();
        var unanswerable = report.Questions.Where(q => !q.Answerable).ToList();
        var common = baseline is null ? null : report.Questions.Select(q => q.Id).Intersect(baseline.Questions.Select(q => q.Id)).ToHashSet();
        var methods = report.Methods;

        sb.Append(Inv, $"# Retrieval eval ({report.Subset})\n\n");
        sb.Append(Inv, $"- Tarih (UTC): {report.CreatedUtc:yyyy-MM-dd HH:mm}\n");
        sb.Append(Inv, $"- Embedding: {report.EmbeddingModel} ({report.EmbeddingDimensions} boyut), {report.ChunkCount} chunk\n");
        sb.Append(Inv, $"- Soru: {report.Questions.Count} ({answerable.Count} cevaplı, {unanswerable.Count} cevapsız; cevapsızlar isabet metriklerine girmez)\n");
        sb.Append(Inv, $"- Embedding önbelleği: {report.CacheHits} isabet, {report.CacheAdded} yeni (NIM'den)\n");
        sb.Append(baseline is null
            ? "- Karşılaştırma: yok\n"
            : string.Create(Inv, $"- Karşılaştırma: {baselineName} ({baseline.CreatedUtc:yyyy-MM-dd}), ortak {common!.Count} soru üzerinden; parantez içi fark\n"));
        sb.Append("\n> Gecikme, `SearchAsync` çağrısının duvar saati süresidir. Embedding ve rerank skorları önbellekten geldiğinde NIM süresi dahil değildir; yalnızca yerel iş ölçülür.\n");
        if (report.CacheAdded > 0)
        {
            sb.Append("> Bu çalıştırmada önbelleğe yeni embedding eklendi: o sorgularda NIM süresi de ölçüldü, gecikmeler diğer çalıştırmalarla karşılaştırılamaz.\n");
        }

        sb.Append("> Hit@20: doğru kaynağın ilk 20 aday içinde olması, yani bir re-ranker'ın en fazla ulaşabileceği oran.\n\n");

        var before = baseline?.Summaries(common).ToDictionary(s => s.Method);
        sb.Append("| Yöntem | Hit@1 | Hit@5 | Hit@20 | MRR | p50 ms | p95 ms |\n|---|---|---|---|---|---|---|\n");
        foreach (var s in report.Summaries())
        {
            var b = before?.GetValueOrDefault(s.Method);
            sb.Append(Inv, $"| {MethodName(s.Method)} | {s.Hit1}/{s.Questions}{Delta(s.Hit1, b?.Hit1)} | {s.Hit5}/{s.Questions}{Delta(s.Hit5, b?.Hit5)} | ")
              .Append(Inv, $"{(s.Hit20 is int h20 ? $"{h20}/{s.Questions}" : "-")} | ")
              .Append(Inv, $"{s.Mrr:F2}{Delta(s.Mrr, b?.Mrr)} | {s.P50Ms:F1}{Delta(s.P50Ms, b?.P50Ms, "F1")} | {s.P95Ms:F1}{Delta(s.P95Ms, b?.P95Ms, "F1")} |\n");
        }

        Breakdown(sb, "Dokümana göre MRR", methods, answerable.GroupBy(q => q.ExpectedDoc!));
        Breakdown(sb, "Etikete göre MRR", methods, answerable.SelectMany(q => q.Tags.Select(t => (Tag: t, Q: q))).GroupBy(x => x.Tag, x => x.Q));

        if (baseline is not null)
        {
            var old = baseline.Questions.ToDictionary(q => q.Id);
            var changed = answerable
                .Where(q => old.TryGetValue(q.Id, out var o) && o.Answerable && Top5(o, RetrievalReport.Hybrid) != Top5(q, RetrievalReport.Hybrid))
                .ToList();
            sb.Append("\n## Sırası değişen sorular (Hybrid, ilk 5)\n\n");
            if (changed.Count == 0)
            {
                sb.Append("Yok.\n");
            }
            else
            {
                sb.Append("| Soru | Önce | Şimdi |\n|---|---|---|\n");
                foreach (var q in changed)
                {
                    sb.Append(Inv, $"| {q.Id}: {q.Question} | {RankText(Top5(old[q.Id], RetrievalReport.Hybrid))} | {RankText(Top5(q, RetrievalReport.Hybrid))} |\n");
                }
            }
        }

        var misses = answerable.Where(q => Top5(q, RetrievalReport.Hybrid) is null).ToList();
        sb.Append("\n## Hybrid'in ilk 5'te bulamadığı sorular\n\n");
        sb.Append(misses.Count == 0 ? "Yok.\n" : string.Concat(misses.Select(q => $"- {q.Id}: {q.Question} → {string.Join("; ", q.HybridTop3)}\n")));

        if (unanswerable.Count > 0)
        {
            sb.Append("\n## Cevabı olmayan sorular: Hybrid'in ilk 3 sonucu\n\n");
            sb.Append(string.Concat(unanswerable.Select(q => $"- {q.Id}: {q.Question} → {string.Join("; ", q.HybridTop3)}\n")));
        }

        sb.Append("\n## Soru bazında sıra (- = aranan derinlikte yok)\n\n| Soru | ")
          .Append(string.Join(" | ", methods.Select(MethodName))).Append(" |\n|---|")
          .Append(string.Concat(methods.Select(_ => "---|"))).Append('\n');
        foreach (var q in answerable)
        {
            sb.Append(Inv, $"| {q.Id}: {q.Question} | ")
              .Append(string.Join(" | ", methods.Select(m => RankText(q.Ranks.GetValueOrDefault(m))))).Append(" |\n");
        }

        return sb.ToString();
    }

    private static void Breakdown(StringBuilder sb, string title, IReadOnlyList<string> methods, IEnumerable<IGrouping<string, QuestionResult>> groups)
    {
        sb.Append(Inv, $"\n## {title}\n\n| Grup | Soru | ").Append(string.Join(" | ", methods.Select(MethodName))).Append(" |\n|---|---|")
          .Append(string.Concat(methods.Select(_ => "---|"))).Append('\n');
        foreach (var g in groups.OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            sb.Append(Inv, $"| {g.Key} | {g.Count()} | ")
              .Append(string.Join(" | ", methods.Select(m => RetrievalMetrics.Summarize(m, g).Mrr.ToString("F2", Inv)))).Append(" |\n");
        }
    }

    /// <summary>The rank if within the top 5, else null: base methods record ranks up to 20, reports from 3.2 up to 5.</summary>
    private static int? Top5(QuestionResult q, string method) =>
        q.Ranks.GetValueOrDefault(method) is int r and <= RetrievalMetrics.TopK ? r : null;

    private static string RankText(int? rank) => rank?.ToString(Inv) ?? "-";

    private static string Delta(double now, double? before, string format = "F2")
    {
        if (before is null)
        {
            return "";
        }

        double d = now - before.Value;
        string formatted = Math.Abs(d).ToString(format, Inv);
        return double.Parse(formatted, Inv) == 0 ? " (=)" : string.Create(Inv, $" ({(d > 0 ? "+" : "-")}{formatted})");
    }

    private static string Delta(int now, int? before) =>
        before is null ? "" : now == before ? " (=)" : string.Create(Inv, $" ({now - before.Value:+0;-0})");
}
