using Assistant.Privacy;
using Assistant.Rag.Search;

namespace Assistant.Core.Safety;

/// <summary>Masks PII in the query and the passages before they are sent to the rerank model.</summary>
public sealed class PiiMaskingReranker : IReranker
{
    private readonly IReranker _inner;
    private readonly IPiiMasker _masker;

    internal PiiMaskingReranker(IReranker inner, IPiiMasker masker)
    {
        _inner = inner;
        _masker = masker;
    }

    public Task<IReadOnlyList<double>> ScoreAsync(string query, IReadOnlyList<string> passages, CancellationToken cancellationToken = default) =>
        _inner.ScoreAsync(_masker.Mask(query), [.. passages.Select(_masker.Mask)], cancellationToken);
}
