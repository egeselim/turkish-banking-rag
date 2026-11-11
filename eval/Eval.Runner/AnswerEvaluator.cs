using System.Diagnostics;
using System.Text;
using Assistant.Core.Chat;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;

namespace Eval.Runner;

/// <summary>
/// Answer mode (step 3.3): each question goes to the real <see cref="BankAssistant"/> in a session without a
/// customer (document questions only). Answerable questions are scored by an LLM judge (Groundedness against the
/// five sources the model got, Completeness against the reference answer, Relevance); every question also gets
/// deterministic checks: does the answer cite, is a cited source an accepted one, did it refuse.
/// </summary>
internal sealed class AnswerEvaluator(
    BankAssistant assistant,
    UsageMeter assistantMeter,
    IReadOnlyList<IEvaluator> judges,
    ChatConfiguration judgeConfiguration,
    UsageMeter judgeMeter)
{
    public const int MaxJudgeAttempts = 3;

    public static IReadOnlyList<IEvaluator> CreateJudges() =>
        [new GroundednessEvaluator(), new CompletenessEvaluator(), new RelevanceEvaluator()];

    public async Task<IReadOnlyList<AnswerResult>> RunAsync(
        IReadOnlyList<EvalQuestion> questions, Action<int, AnswerResult>? progress, CancellationToken cancellationToken)
    {
        var results = new List<AnswerResult>(questions.Count);
        foreach (var question in questions)
        {
            var result = await RunOneAsync(question, cancellationToken).ConfigureAwait(false);
            results.Add(result);
            progress?.Invoke(results.Count, result);
        }

        return results;
    }

    private async Task<AnswerResult> RunOneAsync(EvalQuestion question, CancellationToken cancellationToken)
    {
        assistantMeter.Take();
        judgeMeter.Take();

        string answer = "";
        IReadOnlyList<CitedSource> sources = [];
        string? error = null;
        long start = Stopwatch.GetTimestamp();
        try
        {
            var session = await assistant.CreateSessionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            var turn = await assistant.AskAsync(session, question.Question, cancellationToken).ConfigureAwait(false);
            var text = new StringBuilder();
            await foreach (string part in turn.Text.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                text.Append(part);
            }

            answer = text.ToString().Trim();
            sources = turn.Sources;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            error = "Asistan: " + ex.GetType().Name; // the message may quote the request; only the type is kept
        }

        if (error is null && answer.Length == 0)
        {
            error = "Asistan: boş cevap"; // seen on 2026-11-11 when the free-tier model streamed only a few reasoning tokens
        }

        double latency = Math.Round(Stopwatch.GetElapsedTime(start).TotalMilliseconds, 0);
        var assistantTokens = assistantMeter.Take();

        var cited = Citations.UsedIn(answer, sources);
        var scores = new Dictionary<string, double?>();
        var reasons = new Dictionary<string, string>();
        if (question.Answerable && error is null)
        {
            error = await JudgeAsync(question, answer, sources, scores, reasons, cancellationToken).ConfigureAwait(false);
        }

        return new AnswerResult(
            question.Id,
            question.Question,
            question.ExpectedDoc,
            question.Tags,
            answer,
            [.. cited.Select(s => $"{s.DocId}:{s.Label ?? "-"}")],
            cited.Any(s => question.Accepts(s.DocId, s.Label)),
            IsRefusal(answer),
            scores,
            reasons,
            latency,
            assistantTokens,
            judgeMeter.Take(),
            error);
    }

    /// <summary>
    /// Runs each judge on its own. A metric whose score could not be parsed (the small judge model sometimes leaves
    /// its &lt;S2&gt; tag out, about 8% of calls on 2026-11-11) is retried with that judge, up to <see cref="MaxJudgeAttempts"/>.
    /// </summary>
    private async Task<string?> JudgeAsync(
        EvalQuestion question, string answer, IReadOnlyList<CitedSource> sources,
        Dictionary<string, double?> scores, Dictionary<string, string> reasons, CancellationToken cancellationToken)
    {
        foreach (string name in AnswerReport.Metrics)
        {
            scores[name] = null;
        }

        string? lastError = null;
        foreach (var judge in judges)
        {
            var pending = judge.EvaluationMetricNames.Where(AnswerReport.Metrics.Contains).ToHashSet();
            for (int attempt = 1; attempt <= MaxJudgeAttempts && pending.Count > 0; attempt++)
            {
                try
                {
                    var result = await judge.EvaluateAsync(
                        [new ChatMessage(ChatRole.User, question.Question)],
                        new ChatResponse(new ChatMessage(ChatRole.Assistant, answer)),
                        judgeConfiguration,
                        [new GroundednessEvaluatorContext(GroundingContext(sources)), new CompletenessEvaluatorContext(question.ReferenceAnswer)],
                        cancellationToken).ConfigureAwait(false);

                    foreach (string name in pending.ToList())
                    {
                        if (result.TryGet(name, out NumericMetric? metric) && metric.Value is not null
                            && !(metric.Diagnostics ?? []).Any(d => d.Severity == EvaluationDiagnosticSeverity.Error))
                        {
                            scores[name] = metric.Value;
                            if (metric.Value < 4 && !string.IsNullOrWhiteSpace(metric.Reason))
                            {
                                reasons[name] = metric.Reason;
                            }

                            pending.Remove(name);
                        }
                        else
                        {
                            string? message = (result.TryGet(name, out EvaluationMetric? any) ? any.Diagnostics : null)?
                                .FirstOrDefault(d => d.Severity == EvaluationDiagnosticSeverity.Error)?.Message;
                            lastError = message is null ? $"{name} puanı yok" : Shorten(message);
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    lastError = ex.GetType().Name;
                }
            }
        }

        int missing = scores.Count(s => s.Value is null);
        return missing == 0 ? null : $"Hakem: {missing.ToString(System.Globalization.CultureInfo.InvariantCulture)} metrik {MaxJudgeAttempts} denemede puanlanamadı ({lastError})";
    }

    private static string Shorten(string message)
    {
        string line = message.ReplaceLineEndings(" ");
        return line.Length <= 200 ? line : line[..200] + "…";
    }

    /// <summary>The five passages as the model saw them, numbered, so the judge checks against exactly that.</summary>
    internal static string GroundingContext(IReadOnlyList<CitedSource> sources) =>
        string.Join("\n\n", sources.Select(s => $"[{s.Number}] {s.DocTitle} > {s.Location}\n{s.Text}"));

    internal static bool IsRefusal(string answer) =>
        answer.Contains(AssistantPrompts.NotFoundAnswer.TrimEnd('.'), StringComparison.OrdinalIgnoreCase);
}
