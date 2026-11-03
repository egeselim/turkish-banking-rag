namespace Assistant.Rag.Search;

/// <summary>Finds the source chunks most relevant to a question.</summary>
public interface IDocumentRetriever
{
    Task<IReadOnlyList<RetrievedChunk>> SearchAsync(
        string query, int top = 5, RetrievalMode mode = RetrievalMode.Hybrid, CancellationToken cancellationToken = default);
}
