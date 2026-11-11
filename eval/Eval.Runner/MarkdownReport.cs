using System.Globalization;
using System.Text;
using Assistant.Rag.Search;

namespace Eval.Runner;

/// <summary>Turkish Markdown view of a <see cref="RetrievalReport"/>, optionally against a baseline.</summary>
internal static class MarkdownReport
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string ModeName(RetrievalMode mode) => mode switch
    {
        RetrievalMode.VectorOnly => "Vektör",
        RetrievalMode.KeywordOnly => "BM25",
        RetrievalMode.Hybrid => "Hybrid",
        _ => mode.ToString(),
    };

    public static string Render(RetrievalReport report, RetrievalReport? baseline, string? baselineName)
    {
        var sb = new StringBuilder();
        var answerable = report.Questions.Where(q => q.Answerable).ToList();
        var unanswerable = report.Questions.Where(q => !q.Answerable).ToList();
        var common = baseline is null ? null : report.Questions.Select(q => q.Id).Intersect(baseline.Questions.Select(q => q.Id)).ToHashSet();

        sb.Append(Inv, $"# Retrieval eval ({report.Subset})\n\n");
        sb.Append(Inv, $"- Tarih (UTC): {report.CreatedUtc:yyyy-MM-dd HH:mm}\n");
        sb.Append(Inv, $"- Embedding: {report.EmbeddingModel} ({report.EmbeddingDimensions} boyut), {report.ChunkCount} chunk\n");
        sb.Append(Inv, $"- Soru: {report.Questions.Count} ({answerable.Count} cevaplı, {unanswerable.Count} cevapsız; cevapsızlar isabet metriklerine girmez)\n");
        sb.Append(Inv, $"- Embedding önbelleği: {report.CacheHits} isabet, {report.CacheAdded} yeni (NIM'den)\n");
        sb.Append(baseline is null
            ? "- Karşılaştırma: yok\n"
            : string.Create(Inv, $"- Karşılaştırma: {baselineName} ({baseline.CreatedUtc:yyyy-MM-dd}), ortak {common!.Count} soru üzerinden; parantez içi fark\n"));
        sb.Append("\n> Gecikme, `SearchAsync` çağrısının duvar saati süresidir. Embedding önbellekten geldiğinde NIM süresi dahil değildir; yalnızca yerel arama (Qdrant + BM25) ölçülür.\n");
        if (report.CacheAdded > 0)
        {
            sb.Append("> Bu çalıştırmada önbelleğe yeni embedding eklendi: o sorgularda NIM süresi de ölçüldü, gecikmeler diğer çalıştırmalarla karşılaştırılamaz.\n");
        }

        sb.Append('\n');

        var before = baseline?.Summaries(common).ToDictionary(s => s.Mode);
        sb.Append("| Yöntem | Hit@1 | Hit@5 | MRR | p50 ms | p95 ms |\n|---|---|---|---|---|---|\n");
        foreach (var s in report.Summaries())
        {
            var b = before?.GetValueOrDefault(s.Mode);
            sb.Append(Inv, $"| {ModeName(s.Mode)} | {s.Hit1}/{s.Questions}{Delta(s.Hit1, b?.Hit1)} | {s.Hit5}/{s.Questions}{Delta(s.Hit5, b?.Hit5)} | ")
              .Append(Inv, $"{s.Mrr:F2}{Delta(s.Mrr, b?.Mrr)} | {s.P50Ms:F1}{Delta(s.P50Ms, b?.P50Ms, "F1")} | {s.P95Ms:F1}{Delta(s.P95Ms, b?.P95Ms, "F1")} |\n");
        }

        Breakdown(sb, "Hybrid: dokümana göre", answerable.GroupBy(q => q.ExpectedDoc!));
        Breakdown(sb, "Hybrid: etikete göre", answerable.SelectMany(q => q.Tags.Select(t => (Tag: t, Q: q))).GroupBy(x => x.Tag, x => x.Q));

        if (baseline is not null)
        {
            var old = baseline.Questions.ToDictionary(q => q.Id);
            var changed = answerable
                .Where(q => old.TryGetValue(q.Id, out var o) && o.Answerable && Rank(o) != Rank(q))
                .ToList();
            sb.Append("\n## Sırası değişen sorular (Hybrid)\n\n");
            if (changed.Count == 0)
            {
                sb.Append("Yok.\n");
            }
            else
            {
                sb.Append("| Soru | Önce | Şimdi |\n|---|---|---|\n");
                foreach (var q in changed)
                {
                    sb.Append(Inv, $"| {q.Id}: {q.Question} | {RankText(Rank(old[q.Id]))} | {RankText(Rank(q))} |\n");
                }
            }
        }

        var misses = answerable.Where(q => Rank(q) is null).ToList();
        sb.Append("\n## Hybrid'in ilk 5'te bulamadığı sorular\n\n");
        sb.Append(misses.Count == 0 ? "Yok.\n" : string.Concat(misses.Select(q => $"- {q.Id}: {q.Question} → {string.Join("; ", q.HybridTop3)}\n")));

        if (unanswerable.Count > 0)
        {
            sb.Append("\n## Cevabı olmayan sorular: Hybrid'in ilk 3 sonucu\n\n");
            sb.Append(string.Concat(unanswerable.Select(q => $"- {q.Id}: {q.Question} → {string.Join("; ", q.HybridTop3)}\n")));
        }

        sb.Append("\n## Soru bazında sıra (- = ilk 5'te yok)\n\n| Soru | Vektör | BM25 | Hybrid |\n|---|---|---|---|\n");
        foreach (var q in answerable)
        {
            sb.Append(Inv, $"| {q.Id}: {q.Question} | {RankText(q.Ranks.GetValueOrDefault(RetrievalMode.VectorOnly))} | ")
              .Append(Inv, $"{RankText(q.Ranks.GetValueOrDefault(RetrievalMode.KeywordOnly))} | {RankText(Rank(q))} |\n");
        }

        return sb.ToString();
    }

    private static void Breakdown(StringBuilder sb, string title, IEnumerable<IGrouping<string, QuestionResult>> groups)
    {
        sb.Append(Inv, $"\n## {title}\n\n| Grup | Soru | Hit@1 | Hit@5 | MRR |\n|---|---|---|---|---|\n");
        foreach (var g in groups.OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var s = RetrievalMetrics.Summarize(RetrievalMode.Hybrid, g);
            sb.Append(Inv, $"| {g.Key} | {s.Questions} | {s.Hit1} | {s.Hit5} | {s.Mrr:F2} |\n");
        }
    }

    private static int? Rank(QuestionResult q) => q.Ranks.GetValueOrDefault(RetrievalMode.Hybrid);

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
