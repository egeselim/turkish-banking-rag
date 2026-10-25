using Assistant.Rag.Chunking;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;

namespace Assistant.Rag.Search;

public sealed record IngestionResult(int Added, int Unchanged, int Removed);

/// <summary>
/// The searchable chunk set: vectors in the vector store, keywords in an in-memory BM25 index.
/// <see cref="SyncAsync"/> makes the store match the given chunks and is idempotent: chunk ids are
/// derived from content, so unchanged chunks are neither re-embedded nor rewritten.
/// </summary>
public sealed class ChunkIndex(
    VectorStoreCollection<Guid, ChunkRecord> collection,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
{
    private const int EmbeddingBatchSize = 32;
    private const int MaxRecords = 100_000;

    private volatile KeywordCorpus? _keywords;

    internal sealed record KeywordCorpus(Bm25Index Index, IReadOnlyDictionary<Guid, DocumentChunk> Chunks);

    public VectorStoreCollection<Guid, ChunkRecord> Collection => collection;

    /// <summary>Null until <see cref="SyncAsync"/> has run in this process; retrieval is then vector-only.</summary>
    internal KeywordCorpus? Keywords => _keywords;

    /// <summary>Upserts new chunks, deletes chunks no longer present, and rebuilds the keyword index.</summary>
    public async Task<IngestionResult> SyncAsync(IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunks);

        await collection.EnsureCollectionExistsAsync(cancellationToken).ConfigureAwait(false);

        var existing = new HashSet<Guid>();
        await foreach (var record in collection.GetAsync(r => r.Part >= 1, MaxRecords, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            existing.Add(record.Id);
        }

        var added = chunks.Where(c => !existing.Contains(c.Id)).ToList();
        foreach (var batch in added.Chunk(EmbeddingBatchSize))
        {
            var embeddings = await embeddingGenerator.GenerateAsync(
                batch.Select(c => c.ContextualText), EmbeddingInputType.ForPassage(), cancellationToken).ConfigureAwait(false);
            var records = batch.Zip(embeddings, (chunk, embedding) => ChunkRecord.From(chunk, embedding.Vector));
            await collection.UpsertAsync(records, cancellationToken).ConfigureAwait(false);
        }

        var wanted = chunks.Select(c => c.Id).ToHashSet();
        var stale = existing.Where(id => !wanted.Contains(id)).ToList();
        if (stale.Count > 0)
        {
            await collection.DeleteAsync(stale, cancellationToken).ConfigureAwait(false);
        }

        _keywords = new KeywordCorpus(
            new Bm25Index(chunks.Select(c => (c.Id, c.ContextualText))),
            chunks.ToDictionary(c => c.Id));

        return new IngestionResult(added.Count, chunks.Count - added.Count, stale.Count);
    }
}
