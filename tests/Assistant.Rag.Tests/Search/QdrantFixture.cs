using Assistant.Rag.Chunking;
using Assistant.Rag.Documents;
using Assistant.Rag.Search;
using CommunityToolkit.VectorData.Qdrant;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Qdrant.Client;
using Testcontainers.Qdrant;

namespace Assistant.Rag.Tests.Search;

/// <summary>One throwaway Qdrant container (same image as the AppHost) shared by the integration tests.</summary>
public sealed class QdrantFixture : IAsyncLifetime
{
    private readonly QdrantContainer _container = new QdrantBuilder("qdrant/qdrant:v1.18.0").Build();

    public QdrantClient Client { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        Client = new QdrantClient(new Uri(_container.GetGrpcConnectionString()));
    }

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();
        await _container.DisposeAsync();
    }

    public VectorStoreCollection<Guid, ChunkRecord> NewCollection() =>
        new QdrantCollection<Guid, ChunkRecord>(
            Client,
            "test_" + Guid.NewGuid().ToString("N"),
            ownsClient: false,
            new QdrantCollectionOptions { Definition = ChunkRecord.CreateDefinition(HashingEmbeddingGenerator.Dimensions) });

    public static IReadOnlyList<DocumentChunk> SourceChunks() =>
        [.. Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Sources"), "*.pdf")
            .Order(StringComparer.Ordinal)
            .SelectMany(path => new StructureAwareChunker().Chunk(PdfDocumentReader.Read(path)))];
}

/// <summary>
/// Deterministic stand-in for the embedding model: hashed bag of normalised terms, L2-normalised.
/// Lexical only, but stable across runs, and it counts calls so tests can prove nothing is re-embedded.
/// </summary>
public sealed class HashingEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    public const int Dimensions = 256;

    public int EmbeddedTexts { get; private set; }

    public List<string?> InputTypes { get; } = [];

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        InputTypes.Add(options?.AdditionalProperties?[EmbeddingInputType.PropertyName] as string);
        var embeddings = values.Select(Embed).ToList();
        EmbeddedTexts += embeddings.Count;
        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(embeddings));
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }

    private static Embedding<float> Embed(string text)
    {
        var vector = new float[Dimensions];
        foreach (string term in TurkishTextNormalizer.Terms(text))
        {
            vector[Fnv1a(term) % Dimensions] += 1;
        }

        float norm = MathF.Sqrt(vector.Sum(v => v * v));
        if (norm > 0)
        {
            for (int i = 0; i < vector.Length; i++)
            {
                vector[i] /= norm;
            }
        }

        return new Embedding<float>(vector);
    }

    private static uint Fnv1a(string s)
    {
        uint hash = 2166136261;
        foreach (char c in s)
        {
            hash = (hash ^ c) * 16777619;
        }

        return hash;
    }
}
