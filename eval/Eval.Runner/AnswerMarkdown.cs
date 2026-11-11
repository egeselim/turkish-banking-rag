using System.Globalization;
using System.Text;

namespace Eval.Runner;

/// <summary>Turkish Markdown view of an <see cref="AnswerReport"/>, optionally against a baseline (common questions only).</summary>
internal static class AnswerMarkdown
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Render(AnswerReport report, AnswerReport? baseline, string? baselineName)
    {
        var common = baseline is null ? null : report.Questions.Select(q => q.Id).Intersect(baseline.Questions.Select(q => q.Id)).ToHashSet();
        var now = report.Summarize();
        var before = baseline?.Summarize(common);
        var sb = new StringBuilder();

        sb.Append(Inv, $"# Answer eval ({report.Subset})\n\n");
        sb.Append(Inv, $"- Tarih (UTC): {report.CreatedUtc:yyyy-MM-dd HH:mm}\n");
        sb.Append(Inv, $"- Asistan modeli: {report.ChatModel}; hakem: {report.JudgeModel}; arama: {report.RetrievalMode}\n");
        sb.Append(Inv, $"- Soru: {now.Answerable} cevaplı (hakemle puanlanır), {now.Unanswerable} cevapsız (yalnızca ret kontrolü); hata: {now.Errors}\n");
        sb.Append(baseline is null
            ? "- Karşılaştırma: yok\n"
            : string.Create(Inv, $"- Karşılaştırma: {baselineName} ({baseline.CreatedUtc:yyyy-MM-dd}), ortak {common!.Count} soru üzerinden; parantez içi fark\n"));
        sb.Append("\n> Hakem, asistanla aynı model (ADR-001). Kendi cevabını puanlayan model iyimser olabilir; puanlar mutlak değil, karşılaştırma içindir.\n\n");

        sb.Append("## Hakem puanları (1–5, cevaplı sorular)\n\n| Metrik | Ortalama | ≥4 oranı |\n|---|---|---|\n");
        foreach (string metric in AnswerReport.Metrics)
        {
            double? mean = now.MeanScores[metric];
            double? share = now.ShareAtLeast4[metric];
            sb.Append(Inv, $"| {metric} | {Number(mean, "F2")}{Delta(mean, before?.MeanScores.GetValueOrDefault(metric), "F2")} | ")
              .Append(Inv, $"{Percent(share)}{PercentDelta(share, before?.ShareAtLeast4.GetValueOrDefault(metric))} |\n");
        }

        sb.Append("\n## Deterministik kontroller\n\n| Kontrol | Oran |\n|---|---|\n");
        Row(sb, "Atıf yapan cevap (cevaplı)", now.CitationRate, before?.CitationRate);
        Row(sb, "Atfın doğru kaynağa gitmesi (atıf yapanlarda)", now.CitationAccuracy, before?.CitationAccuracy);
        Row(sb, "Yanlış ret: cevabı olan soruya \"bulamadım\"", now.FalseRefusalRate, before?.FalseRefusalRate);
        Row(sb, "Doğru ret: cevabı olmayan soruya \"bulamadım\"", now.CorrectRefusalRate, before?.CorrectRefusalRate);

        sb.Append("\n## Süre ve token\n\n");
        sb.Append(Inv, $"- Cevap süresi (arama + akışın sonu): p50 {now.P50Ms / 1000:F1} sn{Delta(now.P50Ms / 1000, before?.P50Ms / 1000, "F1")}, p95 {now.P95Ms / 1000:F1} sn{Delta(now.P95Ms / 1000, before?.P95Ms / 1000, "F1")}\n");
        Tokens(sb, "Asistan", now.AssistantTokens, report.Questions.Count, report.Pricing);
        Tokens(sb, "Hakem", now.JudgeTokens, now.Answerable, report.Pricing);
        if (now.AssistantTokens.Calls > 0 && now.AssistantTokens.Input == 0)
        {
            sb.Append("- Not: sağlayıcı bu çağrılarda token kullanımı bildirmedi; token sayıları 0 görünüyor.\n");
        }

        if (report.Pricing is null)
        {
            sb.Append("- Maliyet: fiyat verilmedi (--price-in / --price-out); NIM ücretsiz katmanında gerçek fiyat yok.\n");
        }

        var weak = report.Questions
            .Where(q => q.Answerable && (q.Refused || q.Error is not null || q.Scores.Values.Any(v => v is < 4)))
            .ToList();
        sb.Append("\n## Zayıf cevaplar (bir puan < 4, ret ya da hata)\n\n");
        if (weak.Count == 0)
        {
            sb.Append("Yok.\n");
        }

        foreach (var q in weak)
        {
            string scores = string.Join(", ", AnswerReport.Metrics.Select(m => $"{m} {Number(q.Scores.GetValueOrDefault(m), "F0")}"));
            sb.Append(Inv, $"### {q.Id}: {q.Question}\n\n- {scores}; atıf: {(q.Cited.Count == 0 ? "-" : string.Join(", ", q.Cited))}{(q.Error is null ? "" : "; hata: " + q.Error)}\n");
            foreach (var (metric, reason) in q.Reasons)
            {
                sb.Append(Inv, $"- {metric}: {OneLine(reason)}\n");
            }

            sb.Append(Inv, $"\n> {OneLine(q.Answer)}\n\n");
        }

        var unanswered = report.Questions.Where(q => !q.Answerable).ToList();
        if (unanswered.Count > 0)
        {
            sb.Append("\n## Cevabı olmayan sorular\n\n| Soru | Ret | Cevap |\n|---|---|---|\n");
            foreach (var q in unanswered)
            {
                sb.Append(Inv, $"| {q.Id}: {q.Question} | {(q.Refused ? "evet" : "**hayır**")} | {OneLine(q.Answer)} |\n");
            }
        }

        return sb.ToString();
    }

    private static void Row(StringBuilder sb, string name, double now, double? before) =>
        sb.Append(Inv, $"| {name} | {Percent(now)}{PercentDelta(now, before)} |\n");

    private static void Tokens(StringBuilder sb, string who, TokenUsage usage, int questions, Pricing? pricing)
    {
        double perQuestion = questions == 0 ? 0 : (double)(usage.Input + usage.Output) / questions;
        sb.Append(Inv, $"- {who}: {usage.Calls} çağrı, giriş {usage.Input:N0}, çıkış {usage.Output:N0} token (soru başına {perQuestion:N0})");
        if (pricing is not null)
        {
            sb.Append(Inv, $"; varsayımsal maliyet {pricing.Cost(usage):F4} USD");
        }

        sb.Append('\n');
    }

    private static string OneLine(string text) => text.ReplaceLineEndings(" ").Replace("|", "\\|", StringComparison.Ordinal).Trim();

    private static string Number(double? value, string format) => value?.ToString(format, Inv) ?? "-";

    private static string Percent(double? value) => value is null ? "-" : (value.Value * 100).ToString("F0", Inv) + "%";

    private static string Delta(double? now, double? before, string format)
    {
        if (now is null || before is null)
        {
            return "";
        }

        double d = now.Value - before.Value;
        string formatted = Math.Abs(d).ToString(format, Inv);
        return double.Parse(formatted, Inv) == 0 ? " (=)" : $" ({(d > 0 ? "+" : "-")}{formatted})";
    }

    private static string PercentDelta(double? now, double? before)
    {
        if (now is null || before is null)
        {
            return "";
        }

        double points = Math.Round((now.Value - before.Value) * 100, 0);
        return points == 0 ? " (=)" : string.Create(Inv, $" ({points:+0;-0} puan)");
    }
}
