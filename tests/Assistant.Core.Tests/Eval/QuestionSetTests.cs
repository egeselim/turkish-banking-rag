using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Assistant.Core.Chat;
using Assistant.Core.Retrieval;
using Assistant.Core.Tests.Live;
using Assistant.Rag.Chunking;

namespace Assistant.Core.Tests.Eval;

/// <summary>
/// Step 3.1: eval/questions.jsonl is well formed, every accepted source is a real chunk of the source PDFs, and the
/// numbers in each reference answer appear in those chunks. A changed fee in docs/sources turns this test red until
/// the question is updated too (docs/sources/README.md).
/// </summary>
public sealed partial class QuestionSetTests
{
    private static readonly string[] Documents = ["ucret-tarifesi", "kredi-karti-sozlesmesi", "sss", "kvkk-6698"];

    // The same chunks the app indexes (SourceIngestionService), from the PDFs copied next to the tests.
    private static readonly Lazy<List<DocumentChunk>> Chunks =
        new(() => SourceIngestionService.LoadChunks(Path.Combine(AppContext.BaseDirectory, "Sources")));

    public static TheoryData<string> Ids => [.. QuestionSet.All.Select(q => q.Id)];

    [Fact]
    public void The_set_has_about_a_hundred_unique_questions_in_order()
    {
        var all = QuestionSet.All;

        Assert.InRange(all.Count, 90, 110);
        Assert.Equal(Enumerable.Range(1, all.Count).Select(i => $"q-{i:000}"), all.Select(q => q.Id));
        var duplicates = all.GroupBy(q => q.Question.Trim().ToLower(CultureInfo.GetCultureInfo("tr-TR"))).Where(g => g.Count() > 1).Select(g => g.Key);
        Assert.Empty(duplicates);
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void Question_is_well_formed(string id)
    {
        var q = QuestionSet.Get(id);

        Assert.False(string.IsNullOrWhiteSpace(q.Question));
        Assert.False(string.IsNullOrWhiteSpace(q.ReferenceAnswer));
        Assert.NotEmpty(q.Tags);
        Assert.All(q.Tags, t => Assert.Contains(t, QuestionSet.Tags));
        Assert.Equal(q.Tags.Count, q.Tags.Distinct().Count());
        Assert.Equal(q.ExpectedDoc is null, q.ExpectedSection is null);
        Assert.Equal(!q.Answerable, q.Tags.Contains("unanswerable"));

        if (q.Answerable)
        {
            Assert.Contains(q.ExpectedDoc, Documents);
            Assert.All(q.AlsoAccept, s => Assert.Contains(s.Doc, Documents));
            Assert.Equal(q.AcceptedSources.Count, q.AcceptedSources.Distinct().Count());
            if (q.Tags.Contains("cross_doc"))
            {
                Assert.True(q.AcceptedSources.Select(s => s.Doc).Distinct().Count() > 1, $"{id}: cross_doc needs sources from two documents");
            }
        }
        else
        {
            Assert.Empty(q.AlsoAccept);
            Assert.Equal(AssistantPrompts.NotFoundAnswer, q.ReferenceAnswer);
        }
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void Every_accepted_source_is_a_real_chunk(string id)
    {
        var q = QuestionSet.Get(id);

        Assert.All(q.AcceptedSources, s =>
            Assert.True(Chunks.Value.Any(c => c.DocId == s.Doc && c.Label == s.Section), $"{id}: no chunk {s.Doc} / {s.Section}"));
    }

    // "6,50", "50.000", "08:30": each must appear as a whole number in the accepted chunks' text.
    [Theory]
    [MemberData(nameof(Ids))]
    public void Numbers_in_the_reference_answer_come_from_an_accepted_chunk(string id)
    {
        var q = QuestionSet.Get(id);
        string sourceText = string.Join("\n", Chunks.Value
            .Where(c => q.AcceptedSources.Any(s => s.Doc == c.DocId && s.Section == c.Label))
            .Select(c => c.Text));

        var missing = NumberRegex().Matches(q.ReferenceAnswer)
            .Select(m => m.Value)
            .Where(n => !Regex.IsMatch(sourceText, $@"(?<![\d.,:]){Regex.Escape(n)}(?![\d]|[.,:]\d)", RegexOptions.CultureInvariant))
            .ToList();

        Assert.True(missing.Count == 0, $"{id}: {string.Join(", ", missing)} not in {string.Join("; ", q.AcceptedSources)}");
    }

    [Fact]
    public void Every_article_and_question_of_the_fictional_documents_has_a_question()
    {
        var asked = QuestionSet.All.Where(q => q.Answerable).Select(q => (q.ExpectedDoc, q.ExpectedSection)).ToHashSet();

        var unasked = Chunks.Value
            .Where(c => c.DocId != "kvkk-6698" && c.Label is not null)
            .Select(c => (c.DocId, c.Label))
            .Where(s => !asked.Contains(s!))
            .ToList();

        Assert.Empty(unasked);
    }

    [Fact]
    public void Every_document_and_unanswerable_questions_are_covered()
    {
        var all = QuestionSet.All;

        Assert.All(Documents, d => Assert.True(all.Count(q => q.ExpectedDoc == d) >= 15, d));
        Assert.True(all.Count(q => !q.Answerable) >= 5);
        Assert.True(all.Count(q => q.Tags.Contains("no_diacritics")) >= 3);
        Assert.True(all.Count(q => q.Tags.Contains("cross_doc")) >= 3);
    }

    [Fact]
    public void Smoke_subset_has_about_twenty_questions_from_every_source()
    {
        var smoke = QuestionSet.All.Where(q => q.Smoke).ToList();

        Assert.InRange(smoke.Count, 18, 22);
        Assert.All(Documents, d => Assert.True(smoke.Count(q => q.ExpectedDoc == d) >= 2, d));
        Assert.True(smoke.Count(q => !q.Answerable) >= 2);
        Assert.Contains(smoke, q => q.Tags.Contains("no_diacritics"));
    }

    // Keeps the step 1.5 baseline (Hybrid Hit@1 10/10) comparable with the first runner report in step 3.2.
    [Fact]
    public void The_first_ten_are_the_step_1_5_smoke_questions()
    {
        var legacy = RetrievalSmokeTests.Questions;

        for (int i = 0; i < legacy.Length; i++)
        {
            var q = QuestionSet.All[i];
            Assert.True(q.Smoke, q.Id);
            Assert.Equal(legacy[i].Question, q.Question);
            Assert.Equal(legacy[i].Accept.Select(a => new AcceptedSource(a.Doc, a.Label)), q.AcceptedSources);
        }
    }

    [Theory]
    [InlineData("""{"id":"q-1","question":"x","expected_doc":null,"expected_section":null,"also_accept":[],"reference_answer":"x","smoke":false,"tags":["unanswerable"],"extra":1}""")]
    [InlineData("""{"id":"q-1","question":"x","expected_doc":null,"expected_section":null,"also_accept":[],"reference_answer":"x","tags":["unanswerable"]}""")]
    [InlineData("""{"id":"q-1","question":null,"expected_doc":null,"expected_section":null,"also_accept":[],"reference_answer":"x","smoke":false,"tags":["unanswerable"]}""")]
    [InlineData("""{"id":"q-1","question":"x","expected_doc":"sss","expected_section":"Soru 1","also_accept":[{"doc":"sss"}],"reference_answer":"x","smoke":false,"tags":["fact"]}""")]
    public void Schema_rejects_unknown_missing_and_null_fields(string line)
    {
        Assert.ThrowsAny<JsonException>(() => QuestionSet.Parse(line));
    }

    [GeneratedRegex(@"\d+(?:[.,:]\d+)*")]
    private static partial Regex NumberRegex();
}
