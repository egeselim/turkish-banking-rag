namespace Assistant.Rag.Search;

/// <summary>
/// Immutable in-memory BM25 index over normalised Turkish terms. Rebuilt whole after each ingestion;
/// the corpus (a few hundred chunks) makes that cheap.
/// </summary>
public sealed class Bm25Index
{
    private const double K1 = 1.2;
    private const double B = 0.75;

    private readonly Dictionary<string, List<(int Doc, int Tf)>> _postings = new(StringComparer.Ordinal);
    private readonly Guid[] _ids;
    private readonly int[] _lengths;
    private readonly double _averageLength;

    public Bm25Index(IEnumerable<(Guid Id, string Text)> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);

        var docs = documents.ToList();
        _ids = new Guid[docs.Count];
        _lengths = new int[docs.Count];

        for (int i = 0; i < docs.Count; i++)
        {
            var terms = TurkishTextNormalizer.Terms(docs[i].Text);
            _ids[i] = docs[i].Id;
            _lengths[i] = terms.Count;

            foreach (var group in terms.GroupBy(t => t, StringComparer.Ordinal))
            {
                if (!_postings.TryGetValue(group.Key, out var list))
                {
                    _postings[group.Key] = list = [];
                }

                list.Add((i, group.Count()));
            }
        }

        _averageLength = docs.Count == 0 ? 0 : _lengths.Average();
    }

    public int Count => _ids.Length;

    /// <summary>Top documents for the query, best first; documents sharing no term with the query are not returned.</summary>
    public IReadOnlyList<(Guid Id, double Score)> Search(string query, int top)
    {
        var scores = new Dictionary<int, double>();
        foreach (string term in TurkishTextNormalizer.Terms(query).Distinct(StringComparer.Ordinal))
        {
            if (!_postings.TryGetValue(term, out var postings))
            {
                continue;
            }

            double idf = Math.Log(1 + ((_ids.Length - postings.Count + 0.5) / (postings.Count + 0.5)));
            foreach (var (doc, tf) in postings)
            {
                double norm = tf + (K1 * (1 - B + (B * _lengths[doc] / _averageLength)));
                scores[doc] = scores.GetValueOrDefault(doc) + (idf * tf * (K1 + 1) / norm);
            }
        }

        return [.. scores.OrderByDescending(s => s.Value).ThenBy(s => s.Key).Take(top).Select(s => (_ids[s.Key], s.Value))];
    }
}
