using Assistant.Privacy;
using Microsoft.Extensions.AI;

namespace Assistant.Core.Safety;

/// <summary>Masks PII in every text before it is embedded (search queries can contain user input).</summary>
internal sealed class PiiMaskingEmbeddingGenerator(IEmbeddingGenerator<string, Embedding<float>> innerGenerator, IPiiMasker masker)
    : DelegatingEmbeddingGenerator<string, Embedding<float>>(innerGenerator)
{
    public override Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default) =>
        base.GenerateAsync([.. values.Select(masker.Mask)], options, cancellationToken);
}
