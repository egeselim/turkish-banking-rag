using System.Runtime.CompilerServices;
using Assistant.Core.Chat;
using Assistant.Rag.Chunking;
using Assistant.Rag.Search;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace Eval.Runner.Tests;

public sealed class AnswerTests
{
    private static readonly EvalQuestion Eft = new(
        "q-001", "Başka bankaya mobilden para göndermek kaç lira?", "ucret-tarifesi", "Madde 4",
        [new AcceptedSource("sss", "Soru 10")], "Mobil EFT 6,50 TL'dir.", true, ["fact"]);

    private static readonly EvalQuestion Ceo = new(
        "q-093", "Selim Bank'ın genel müdürü kim?", null, null, [], AssistantPrompts.NotFoundAnswer, true, ["unanswerable"]);

    [Theory]
    [InlineData("Bu konuda dokümanlarımızda bilgi bulamadım.", true)]
    [InlineData("Üzgünüm, bu konuda dokümanlarımızda bilgi bulamadım", true)]
    [InlineData("Mobil EFT 6,50 TL'dir [1].", false)]
    public void Refusal_is_the_assistants_not_found_sentence(string answer, bool refused)
    {
        Assert.Equal(refused, AnswerEvaluator.IsRefusal(answer));
    }

    // End to end on the real BankAssistant with a scripted model, a fixed retriever and a fake judge: no API key.
    [Fact]
    public async Task Answerable_question_is_judged_with_the_sources_and_the_reference_answer()
    {
        var model = new ScriptedModel("Mobil EFT 6,50 TL'dir [1].");
        var judge = new RecordingJudge();
        var evaluator = Evaluator(model, judge);

        var result = Assert.Single(await evaluator.RunAsync([Eft], null, TestContext.Current.CancellationToken));

        Assert.Equal("Mobil EFT 6,50 TL'dir [1].", result.Answer);
        Assert.Equal(["ucret-tarifesi:Madde 4"], result.Cited);
        Assert.True(result.CitationCorrect);
        Assert.False(result.Refused);
        Assert.All(AnswerReport.Metrics, m => Assert.Equal(5, result.Scores[m]));
        Assert.Equal(new TokenUsage(100, 10, 1), result.AssistantTokens);
        Assert.Null(result.Error);

        Assert.Equal(Eft.Question, judge.Question);
        Assert.Contains("[1] Selim Bank Bireysel Ücret ve Komisyon Tarifesi", judge.Grounding, StringComparison.Ordinal);
        Assert.Contains("Mobil ve internetten EFT: 6,50 TL.", judge.Grounding, StringComparison.Ordinal);
        Assert.Equal(Eft.ReferenceAnswer, judge.GroundTruth);
    }

    [Fact]
    public async Task Unanswerable_question_is_not_judged_only_checked_for_refusal()
    {
        var judge = new RecordingJudge();
        var evaluator = Evaluator(new ScriptedModel(AssistantPrompts.NotFoundAnswer), judge);

        var result = Assert.Single(await evaluator.RunAsync([Ceo], null, TestContext.Current.CancellationToken));

        Assert.True(result.Refused);
        Assert.Empty(result.Scores);
        Assert.Equal(0, judge.Calls);
    }

    [Fact]
    public async Task Empty_answer_is_an_error_and_is_not_judged()
    {
        var judge = new RecordingJudge();
        var evaluator = Evaluator(new ScriptedModel(""), judge);

        var result = Assert.Single(await evaluator.RunAsync([Eft], null, TestContext.Current.CancellationToken));

        Assert.Equal("Asistan: boş cevap", result.Error);
        Assert.Equal(0, judge.Calls);
    }

    [Fact]
    public async Task A_failing_judge_is_recorded_not_thrown()
    {
        var evaluator = Evaluator(new ScriptedModel("Cevap [1]."), new RecordingJudge { Fail = true });

        var result = Assert.Single(await evaluator.RunAsync([Eft], null, TestContext.Current.CancellationToken));

        Assert.Equal("Hakem: 3 metrik 3 denemede puanlanamadı (HttpRequestException)", result.Error);
        Assert.All(AnswerReport.Metrics, m => Assert.Null(result.Scores[m]));
    }

    // The small judge sometimes leaves out its score tag; the metric is asked again instead of being lost.
    [Fact]
    public async Task A_metric_without_a_score_is_retried()
    {
        var judge = new RecordingJudge { MissingOnFirstCall = "Groundedness" };
        var evaluator = Evaluator(new ScriptedModel("Mobil EFT 6,50 TL'dir [1]."), judge);

        var result = Assert.Single(await evaluator.RunAsync([Eft], null, TestContext.Current.CancellationToken));

        Assert.Null(result.Error);
        Assert.Equal(5, result.Scores["Groundedness"]);
        Assert.Equal(2, judge.Calls);
    }

    [Fact]
    public void Summary_rates_follow_hand_computed_values()
    {
        // Answerable: a (cites correctly, 5/5/5), b (cites wrongly, 3/4/null), c (refuses). Unanswerable: u refuses, v answers.
        var report = Report(
            Result("a", answerable: true, cited: ["x"], correct: true, refused: false, 5, 5, 5),
            Result("b", answerable: true, cited: ["y"], correct: false, refused: false, 3, 4, null),
            Result("c", answerable: true, cited: [], correct: false, refused: true, 1, 1, 2),
            Result("u", answerable: false, cited: [], correct: false, refused: true),
            Result("v", answerable: false, cited: [], correct: false, refused: false));

        var s = report.Summarize();

        Assert.Equal(3, s.Answerable);
        Assert.Equal(3.0, s.MeanScores["Groundedness"]);
        Assert.Equal(3.5, s.MeanScores["Relevance"]);  // null skipped
        Assert.Equal(2.0 / 3, s.ShareAtLeast4["Completeness"]);
        Assert.Equal(2.0 / 3, s.CitationRate);
        Assert.Equal(0.5, s.CitationAccuracy);
        Assert.Equal(1.0 / 3, s.FalseRefusalRate);
        Assert.Equal(0.5, s.CorrectRefusalRate);
        Assert.Equal(new TokenUsage(500, 50, 5), s.AssistantTokens);
    }

    [Fact]
    public void Json_round_trip_and_markdown_with_baseline_and_price()
    {
        var baseline = Report(Result("a", true, ["x"], true, false, 3, 3, 3));
        var report = Report(Result("a", true, ["x"], true, false, 5, 5, 5)) with { Pricing = new Pricing(1000, 2000) };

        var back = AnswerReport.FromJson(report.ToJson());
        string markdown = AnswerMarkdown.Render(back, baseline, "baseline-answer.json");

        Assert.Equal(report.Summarize().MeanScores, back.Summarize().MeanScores);
        Assert.Contains("| Groundedness | 5.00 (+2.00) | 100% (+100 puan) |", markdown, StringComparison.Ordinal);
        Assert.Contains("varsayımsal maliyet 0.1200 USD", markdown, StringComparison.Ordinal); // (100*1000 + 10*2000) / 1e6
        Assert.Contains("ortak 1 soru", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Markdown_lists_weak_answers_with_the_judges_reason()
    {
        var weak = Result("b", true, [], false, false, 2, 5, 5) with { Reasons = new Dictionary<string, string> { ["Groundedness"] = "Kaynakta yok." } };

        string markdown = AnswerMarkdown.Render(Report(weak), null, null);

        Assert.Contains("### b: soru b", markdown, StringComparison.Ordinal);
        Assert.Contains("- Groundedness: Kaynakta yok.", markdown, StringComparison.Ordinal);
        Assert.Contains("fiyat verilmedi", markdown, StringComparison.Ordinal);
    }

    private static AnswerEvaluator Evaluator(ScriptedModel model, RecordingJudge judge)
    {
        var assistantMeter = new UsageMeter(model);
        var judgeMeter = new UsageMeter(model);
        var assistant = new BankAssistant(assistantMeter, new FixedRetriever());
        return new AnswerEvaluator(assistant, assistantMeter, [judge], new ChatConfiguration(judgeMeter), judgeMeter);
    }

    private static AnswerReport Report(params AnswerResult[] results) =>
        new(AnswerReport.CurrentSchemaVersion, DateTimeOffset.UnixEpoch, "test", "chat", "chat", "Hybrid", null, results);

    private static AnswerResult Result(string id, bool answerable, string[] cited, bool correct, bool refused, params double?[] scores) => new(
        id,
        "soru " + id,
        answerable ? "sss" : null,
        [answerable ? "fact" : "unanswerable"],
        "cevap " + id,
        cited,
        correct,
        refused,
        answerable ? AnswerReport.Metrics.Select((m, i) => (m, scores[i])).ToDictionary(x => x.m, x => x.Item2) : new Dictionary<string, double?>(),
        new Dictionary<string, string>(),
        1000,
        new TokenUsage(100, 10, 1),
        TokenUsage.None,
        null);

    private sealed class FixedRetriever : IDocumentRetriever
    {
        public Task<IReadOnlyList<RetrievedChunk>> SearchAsync(string query, int top = 5, RetrievalMode mode = RetrievalMode.Hybrid, CancellationToken cancellationToken = default)
        {
            var chunk = new DocumentChunk(
                Guid.NewGuid(), "ucret-tarifesi", "Selim Bank Bireysel Ücret ve Komisyon Tarifesi", ["Bölüm 2", "Madde 4 – EFT Ücretleri"],
                "Madde 4", 1, 1, 0, 1, 1, "Mobil ve internetten EFT: 6,50 TL.");
            return Task.FromResult<IReadOnlyList<RetrievedChunk>>([new RetrievedChunk(chunk, 1, 1, 1)]);
        }
    }

    /// <summary>Streams one fixed answer and reports usage, as NIM does at the end of a stream.</summary>
    private sealed class ScriptedModel(string answer) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, answer)));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, answer);
            yield return new ChatResponseUpdate { Role = ChatRole.Assistant, Contents = [new UsageContent(new UsageDetails { InputTokenCount = 100, OutputTokenCount = 10 })] };
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class RecordingJudge : IEvaluator
    {
        public bool Fail { get; init; }

        public string? MissingOnFirstCall { get; init; }

        public int Calls { get; private set; }

        public string? Question { get; private set; }

        public string? Grounding { get; private set; }

        public string? GroundTruth { get; private set; }

        public IReadOnlyCollection<string> EvaluationMetricNames => AnswerReport.Metrics;

        public ValueTask<EvaluationResult> EvaluateAsync(
            IEnumerable<ChatMessage> messages, ChatResponse modelResponse, ChatConfiguration? chatConfiguration = null,
            IEnumerable<EvaluationContext>? additionalContext = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Fail)
            {
                throw new HttpRequestException("judge down");
            }

            Question = messages.Single().Text;
            var contexts = additionalContext!.ToList();
            Grounding = string.Concat(contexts.OfType<Microsoft.Extensions.AI.Evaluation.Quality.GroundednessEvaluatorContext>().Single().GroundingContext);
            GroundTruth = contexts.OfType<Microsoft.Extensions.AI.Evaluation.Quality.CompletenessEvaluatorContext>().Single().GroundTruth;
            return ValueTask.FromResult(new EvaluationResult(AnswerReport.Metrics.Select(m =>
                (EvaluationMetric)new NumericMetric(m, Calls == 1 && m == MissingOnFirstCall ? null : 5))));
        }
    }
}
