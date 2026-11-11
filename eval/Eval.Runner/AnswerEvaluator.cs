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
    IEvaluator judge,
    ChatConfiguration judgeConfiguration,
    UsageMeter judgeMeter)
{
    public static IEvaluator CreateJudge() =>
        new CompositeEvaluator(new GroundednessEvaluator(), new CompletenessEvaluator(), new RelevanceEvaluator());

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

    private async Task<string?> JudgeAsync(
        EvalQuestion question, string answer, IReadOnlyList<CitedSource> sources,
        Dictionary<string, double?> scores, Dictionary<string, string> reasons, CancellationToken cancellationToken)
    {
        try
        {
            var result = await judge.EvaluateAsync(
                [new ChatMessage(ChatRole.User, question.Question)],
                new ChatResponse(new ChatMessage(ChatRole.Assistant, answer)),
                judgeConfiguration,
                [new GroundednessEvaluatorContext(GroundingContext(sources)), new CompletenessEvaluatorContext(question.ReferenceAnswer)],
                cancellationToken).ConfigureAwait(false);

            foreach (string name in AnswerReport.Metrics)
            {
                if (result.TryGet(name, out NumericMetric? metric))
                {
                    scores[name] = metric.Value;
                    if (metric.Value is < 4 && !string.IsNullOrWhiteSpace(metric.Reason))
                    {
                        reasons[name] = metric.Reason;
                    }
                }
                else
                {
                    scores[name] = null;
                }
            }

            var errors = result.Metrics.Values
                .SelectMany(m => m.Diagnostics ?? [])
                .Where(d => d.Severity == EvaluationDiagnosticSeverity.Error)
                .ToList();
            if (errors.Count == 0)
            {
                return null;
            }

            string first = errors[0].Message.ReplaceLineEndings(" ");
            return $"Hakem: {errors.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} tanı hatası ({(first.Length <= 200 ? first : first[..200] + "…")})";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            foreach (string name in AnswerReport.Metrics)
            {
                scores[name] = null;
            }

            return "Hakem: " + ex.GetType().Name;
        }
    }

    /// <summary>The five passages as the model saw them, numbered, so the judge checks against exactly that.</summary>
    internal static string GroundingContext(IReadOnlyList<CitedSource> sources) =>
        string.Join("\n\n", sources.Select(s => $"[{s.Number}] {s.DocTitle} > {s.Location}\n{s.Text}"));

    internal static bool IsRefusal(string answer) =>
        answer.Contains(AssistantPrompts.NotFoundAnswer.TrimEnd('.'), StringComparison.OrdinalIgnoreCase);
}
