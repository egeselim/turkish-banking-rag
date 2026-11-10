using System.Text.Json;
using System.Text.Json.Serialization;

namespace Assistant.Core.Tests.Eval;

/// <summary>A source that counts as a correct retrieval hit: document id and citation label ("Madde 4", "Soru 10").</summary>
public sealed record AcceptedSource(string Doc, string Section);

/// <summary>
/// One question from eval/questions.jsonl. <see cref="ExpectedDoc"/> and <see cref="ExpectedSection"/> name the
/// primary source; <see cref="AlsoAccept"/> lists other chunks that answer it just as well. Both are null for
/// questions the documents do not answer ("unanswerable"), whose reference answer is the assistant's not-found text.
/// </summary>
public sealed record EvalQuestion(
    string Id,
    string Question,
    string? ExpectedDoc,
    string? ExpectedSection,
    IReadOnlyList<AcceptedSource> AlsoAccept,
    string ReferenceAnswer,
    bool Smoke,
    IReadOnlyList<string> Tags)
{
    public bool Answerable => ExpectedDoc is not null;

    /// <summary>The primary source followed by <see cref="AlsoAccept"/>; empty for unanswerable questions.</summary>
    public IReadOnlyList<AcceptedSource> AcceptedSources =>
        Answerable ? [new AcceptedSource(ExpectedDoc!, ExpectedSection!), .. AlsoAccept] : [];

    public override string ToString() => Id;
}

/// <summary>
/// Loads eval/questions.jsonl. The serializer is the schema check: unknown fields, missing fields and nulls in
/// non-nullable fields all throw. The eval runner (step 3.2) reads the same file.
/// </summary>
public static class QuestionSet
{
    public static readonly string[] Tags =
        ["fact", "procedure", "definition", "paraphrase", "no_diacritics", "cross_doc", "unanswerable"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    private static readonly Lazy<IReadOnlyList<EvalQuestion>> LazyAll = new(Load);

    public static IReadOnlyList<EvalQuestion> All => LazyAll.Value;

    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "Eval", "questions.jsonl");

    public static EvalQuestion Get(string id) => All.Single(q => q.Id == id);

    public static EvalQuestion Parse(string line) =>
        JsonSerializer.Deserialize<EvalQuestion>(line, Json) ?? throw new InvalidDataException("Empty line in questions.jsonl.");

    private static List<EvalQuestion> Load() =>
        [.. File.ReadLines(FilePath).Where(line => !string.IsNullOrWhiteSpace(line)).Select(Parse)];
}
