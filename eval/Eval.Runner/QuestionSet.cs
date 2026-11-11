using System.Text.Json;
using System.Text.Json.Serialization;

namespace Eval.Runner;

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

    public bool Accepts(string docId, string? label) => AcceptedSources.Any(s => s.Doc == docId && s.Section == label);

    public override string ToString() => Id;
}

/// <summary>
/// Reads eval/questions.jsonl. The serializer is the schema check: unknown fields, missing fields and nulls in
/// non-nullable fields all throw.
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

    public static EvalQuestion Parse(string line) =>
        JsonSerializer.Deserialize<EvalQuestion>(line, Json) ?? throw new InvalidDataException("Empty line in questions.jsonl.");

    public static IReadOnlyList<EvalQuestion> Load(string path) =>
        [.. File.ReadLines(path).Where(line => !string.IsNullOrWhiteSpace(line)).Select(Parse)];
}
