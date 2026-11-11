using System.Diagnostics;
using Assistant.Rag.Search;

namespace Eval.Runner;

/// <summary>Runs each question through every retrieval mode and records where the first accepted source appears.</summary>
internal static class RetrievalEvaluator
{
    public static async Task<IReadOnlyList<QuestionResult>> RunAsync(
        IDocumentRetriever retriever, IReadOnlyList<EvalQuestion> questions, CancellationToken cancellationToken)
    {
        // Warm-up, not measured: the first call pays for JIT, connection setup and the BM25 index's first use.
        if (questions.Count > 0)
        {
            foreach (var mode in RetrievalReport.Modes)
            {
                await retriever.SearchAsync(questions[0].Question, RetrievalMetrics.TopK, mode, cancellationToken).ConfigureAwait(false);
            }
        }

        var results = new List<QuestionResult>(questions.Count);
        foreach (var question in questions)
        {
            var ranks = new Dictionary<RetrievalMode, int?>();
            var latency = new Dictionary<RetrievalMode, double>();
            IReadOnlyList<RetrievedChunk> hybrid = [];

            foreach (var mode in RetrievalReport.Modes)
            {
                long start = Stopwatch.GetTimestamp();
                var hits = await retriever.SearchAsync(question.Question, RetrievalMetrics.TopK, mode, cancellationToken).ConfigureAwait(false);
                latency[mode] = Math.Round(Stopwatch.GetElapsedTime(start).TotalMilliseconds, 2);

                if (question.Answerable)
                {
                    int index = hits.ToList().FindIndex(h => question.Accepts(h.Chunk.DocId, h.Chunk.Label));
                    ranks[mode] = index < 0 ? null : index + 1;
                }

                if (mode == RetrievalMode.Hybrid)
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
                [.. hybrid.Take(3).Select(h => $"{h.Chunk.DocId}:{h.Chunk.Label ?? "-"}")]));
        }

        return results;
    }
}
