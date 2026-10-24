using Assistant.Core.Safety;
using Assistant.Privacy;
using Assistant.Privacy.Tests.TestData;
using Assistant.Rag;
using Microsoft.Extensions.AI;

namespace Assistant.Core.Tests.Safety;

public class PiiMaskingEmbeddingGeneratorTests
{
    [Fact]
    public async Task Masks_values_and_keeps_options()
    {
        string tckn = FakePii.Tckn(FakePii.Seeded());
        var inner = new RecordingEmbeddingGenerator();
        using var generator = new PiiMaskingEmbeddingGenerator(inner, new PiiMasker());
        var options = EmbeddingInputType.ForQuery();

        await generator.GenerateAsync([$"{tckn} numaralı müşterinin kartı", "EFT ücreti"], options, TestContext.Current.CancellationToken);

        Assert.Equal(["[TCKN] numaralı müşterinin kartı", "EFT ücreti"], inner.Values);
        Assert.Same(options, inner.Options);
    }

    private sealed class RecordingEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        private static readonly float[] DummyVector = [1f];

        public List<string> Values { get; private set; } = [];

        public EmbeddingGenerationOptions? Options { get; private set; }

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
        {
            Values = [.. values];
            Options = options;
            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(Values.Select(_ => new Embedding<float>(DummyVector))));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
