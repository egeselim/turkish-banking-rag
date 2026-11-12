using System.Diagnostics;
using Assistant.Rag.Search;

namespace Eval.Runner;

/// <summary>A retrieval method under test: a name for the report, the retriever, the mode passed to it and how deep to search.</summary>
internal sealed record RetrievalMethod(string Name, IDocumentRetriever Retriever, RetrievalMode Mode, int Depth);

/// <summary>Runs each question through every method and records where the first accepted source appears.</summary>
internal static class RetrievalEvaluator
{
    /// <summary>The three base methods on one retriever, searched <see cref="RetrievalMetrics.CandidateDepth"/> deep.</summary>
    public static IReadOnlyList<RetrievalMethod> BaseMethods(IDocumentRetriever retriever) =>
    [
        new("VectorOnly", retriever, RetrievalMode.VectorOnly, RetrievalMetrics.CandidateDepth),
        new("KeywordOnly", retriever, RetrievalMode.KeywordOnly, RetrievalMetrics.CandidateDepth),
        new(RetrievalReport.Hybrid, retriever, RetrievalMode.Hybrid, RetrievalMetrics.CandidateDepth),
    ];

    public static Task<IReadOnlyList<QuestionResult>> RunAsync(
        IDocumentRetriever retriever, IReadOnlyList<EvalQuestion> questions, CancellationToken cancellationToken) =>
        RunAsync(BaseMethods(retriever), questions, cancellationToken);

    public static async Task<IReadOnlyList<QuestionResult>> RunAsync(
        IReadOnlyList<RetrievalMethod> methods, IReadOnlyList<EvalQuestion> questions, CancellationToken cancellationToken)
    {
        // Warm-up, not measured: the first call pays for JIT, connection setup and the BM25 index's first use.
        if (questions.Count > 0)
        {
            foreach (var method in methods)
            {
                await method.Retriever.SearchAsync(questions[0].Question, method.Depth, method.Mode, cancellationToken).ConfigureAwait(false);
            }
        }

        var results = new List<QuestionResult>(questions.Count);
        foreach (var question in questions)
        {
            var ranks = new Dictionary<string, int?>();
            var latency = new Dictionary<string, double>();
            var depths = new Dictionary<string, int>();
            IReadOnlyList<RetrievedChunk> hybrid = [];

            foreach (var method in methods)
            {
                long start = Stopwatch.GetTimestamp();
                var hits = await method.Retriever.SearchAsync(question.Question, method.Depth, method.Mode, cancellationToken).ConfigureAwait(false);
                latency[method.Name] = Math.Round(Stopwatch.GetElapsedTime(start).TotalMilliseconds, 2);
                depths[method.Name] = method.Depth;

                if (question.Answerable)
                {
                    int index = hits.ToList().FindIndex(h => question.Accepts(h.Chunk.DocId, h.Chunk.Label));
                    ranks[method.Name] = index < 0 ? null : index + 1;
                }

                if (method.Name == RetrievalReport.Hybrid)
                {
                    hybrid = hits;
                }
            }

            results.Add(new QuestionResult(
                question.Id,
                question.Question,
                question.ExpectedDoc,
                question.Tags,
                ranks,
                latency,
                [.. hybrid.Take(3).Select(h => $"{h.Chunk.DocId}:{h.Chunk.Label ?? "-"}")],
                depths));
        }

        return results;
    }
}
