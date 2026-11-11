using Assistant.Rag.Chunking;
using Assistant.Rag.Documents;

namespace Assistant.Core.Retrieval;

/// <summary>The source PDFs as the app indexes them. Shared by ingestion and the eval runner, so both see the same chunks.</summary>
public static class SourceDocuments
{
    /// <summary>Reads and chunks every PDF in the folder, in file-name order.</summary>
    public static List<DocumentChunk> LoadChunks(string sourcesPath)
    {
        if (string.IsNullOrWhiteSpace(sourcesPath) || !Directory.Exists(sourcesPath))
        {
            throw new InvalidOperationException($"Rag:SourcesPath '{sourcesPath}' is not an existing folder.");
        }

        var chunker = new StructureAwareChunker();
        return [.. Directory.EnumerateFiles(sourcesPath, "*.pdf")
            .Order(StringComparer.Ordinal)
            .SelectMany(path => chunker.Chunk(PdfDocumentReader.Read(path)))];
    }
}
