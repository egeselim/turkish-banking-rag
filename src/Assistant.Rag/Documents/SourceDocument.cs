namespace Assistant.Rag.Documents;

public enum BlockKind
{
    Heading,
    Paragraph,
}

/// <summary>A heading or paragraph recovered from a source document, in reading order.</summary>
/// <param name="Level">Heading level (1 = most prominent); 0 for paragraphs.</param>
public sealed record DocumentBlock(BlockKind Kind, string Text, int PageStart, int PageEnd, int Level = 0);

/// <summary>A parsed source document. <paramref name="DocId"/> is stable (file name without extension).</summary>
public sealed record SourceDocument(string DocId, string Title, IReadOnlyList<DocumentBlock> Blocks);
