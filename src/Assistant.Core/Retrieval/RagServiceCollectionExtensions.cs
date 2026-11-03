using System.Text.RegularExpressions;
using Assistant.Core.Models;
using Assistant.Rag.Search;
using CommunityToolkit.VectorData.Qdrant;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.VectorData;
using Qdrant.Client;

namespace Assistant.Core.Retrieval;

public static partial class RagServiceCollectionExtensions
{
    /// <summary>
    /// Registers the chunk collection (Qdrant), <see cref="ChunkIndex"/> and <see cref="HybridRetriever"/>.
    /// Requires a <see cref="QdrantClient"/> (e.g. Aspire's <c>AddQdrantClient</c>) and <see cref="ModelServiceCollectionExtensions.AddNimModels"/>.
    /// </summary>
    public static IServiceCollection AddRag(this IServiceCollection services)
    {
        services.AddSingleton<VectorStoreCollection<Guid, ChunkRecord>>(sp =>
        {
            var nim = sp.GetRequiredService<IOptions<NimOptions>>().Value;
            nim.ValidateEmbeddingDimensions();
            return new QdrantCollection<Guid, ChunkRecord>(
                sp.GetRequiredService<QdrantClient>(),
                CollectionName(nim.EmbeddingModel, nim.EmbeddingDimensions),
                ownsClient: false,
                new QdrantCollectionOptions { Definition = ChunkRecord.CreateDefinition(nim.EmbeddingDimensions) });
        });
        services.AddSingleton<ChunkIndex>();
        services.AddSingleton(sp => new HybridRetriever(
            sp.GetRequiredService<ChunkIndex>(),
            sp.GetRequiredService<Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>>()));
        services.AddSingleton<IDocumentRetriever>(sp => sp.GetRequiredService<HybridRetriever>());

        return services;
    }

    /// <summary>
    /// Ingests the PDFs in <c>Rag:SourcesPath</c> at startup (background) and exposes progress as <see cref="IngestionStatus"/>.
    /// </summary>
    public static IServiceCollection AddSourceIngestion(this IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        services.AddOptions<RagOptions>().Bind(configuration.GetSection(RagOptions.SectionName));
        services.AddSingleton<IngestionStatus>();
        services.AddHostedService<SourceIngestionService>();
        return services;
    }

    /// <summary>
    /// One collection per embedding model and size, e.g. <c>chunks_nvidia_nemotron_3_5_embed_1b_2048</c>,
    /// so switching models never mixes incompatible vectors.
    /// </summary>
    public static string CollectionName(string embeddingModel, int dimensions) =>
        $"chunks_{NonAlphanumericRegex().Replace(embeddingModel.ToLowerInvariant(), "_").Trim('_')}_{dimensions}";

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumericRegex();
}
