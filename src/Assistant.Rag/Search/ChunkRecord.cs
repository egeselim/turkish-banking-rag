using Assistant.Rag.Chunking;
using Microsoft.Extensions.VectorData;

namespace Assistant.Rag.Search;

/// <summary>
/// Storage shape of a <see cref="DocumentChunk"/> in the vector store. The schema is defined in code
/// (<see cref="CreateDefinition"/>) because the vector size depends on the configured embedding model.
/// </summary>
public sealed class ChunkRecord
{
    private const string PathSeparator = " > ";

    public Guid Id { get; set; }

    public string DocId { get; set; } = "";

    public string DocTitle { get; set; } = "";

    public string SectionPath { get; set; } = "";

    public string? Label { get; set; }

    public int PageStart { get; set; }

    public int PageEnd { get; set; }

    public int Index { get; set; }

    public int Part { get; set; }

    public int PartCount { get; set; }

    public string Text { get; set; } = "";

    /// <summary>Normalised terms of the contextual text; full-text indexed for the store's own keyword search.</summary>
    public string SearchText { get; set; } = "";

    public ReadOnlyMemory<float> Embedding { get; set; }

    public static VectorStoreCollectionDefinition CreateDefinition(int dimensions) => new()
    {
        Properties =
        [
            new VectorStoreKeyProperty(nameof(Id), typeof(Guid)),
            new VectorStoreDataProperty(nameof(DocId), typeof(string)) { IsIndexed = true },
            new VectorStoreDataProperty(nameof(DocTitle), typeof(string)),
            new VectorStoreDataProperty(nameof(SectionPath), typeof(string)),
            new VectorStoreDataProperty(nameof(Label), typeof(string)),
            new VectorStoreDataProperty(nameof(PageStart), typeof(int)),
            new VectorStoreDataProperty(nameof(PageEnd), typeof(int)),
            new VectorStoreDataProperty(nameof(Index), typeof(int)),
            new VectorStoreDataProperty(nameof(Part), typeof(int)) { IsIndexed = true },
            new VectorStoreDataProperty(nameof(PartCount), typeof(int)),
            new VectorStoreDataProperty(nameof(Text), typeof(string)),
            new VectorStoreDataProperty(nameof(SearchText), typeof(string)) { IsFullTextIndexed = true },
            new VectorStoreVectorProperty(nameof(Embedding), typeof(ReadOnlyMemory<float>), dimensions)
            {
                DistanceFunction = DistanceFunction.CosineSimilarity,
            },
        ],
    };

    public static ChunkRecord From(DocumentChunk chunk, ReadOnlyMemory<float> embedding) => new()
    {
        Id = chunk.Id,
        DocId = chunk.DocId,
        DocTitle = chunk.DocTitle,
        SectionPath = string.Join(PathSeparator, chunk.SectionPath),
        Label = chunk.Label,
        PageStart = chunk.PageStart,
        PageEnd = chunk.PageEnd,
        Index = chunk.Index,
        Part = chunk.Part,
        PartCount = chunk.PartCount,
        Text = chunk.Text,
        SearchText = string.Join(' ', TurkishTextNormalizer.Terms(chunk.ContextualText)),
        Embedding = embedding,
    };

    public DocumentChunk ToChunk() => new(
        Id,
        DocId,
        DocTitle,
        SectionPath.Length == 0 ? [] : SectionPath.Split(PathSeparator),
        Label,
        PageStart,
        PageEnd,
        Index,
        Part,
        PartCount,
        Text);
}
