using Assistant.Core.Models;
using Assistant.Core.Safety;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Assistant.Core.Tests.Models;

public class ModelServiceCollectionExtensionsTests
{
    [Fact]
    public void Falls_back_to_NVIDIA_API_KEY_when_Nim_ApiKey_is_missing()
    {
        using var sp = Build(new() { ["NVIDIA_API_KEY"] = "nvapi-from-env" });

        Assert.Equal("nvapi-from-env", sp.GetRequiredService<IOptions<NimOptions>>().Value.ApiKey);
    }

    [Fact]
    public void Resolves_main_small_and_embedding_clients_from_configuration()
    {
        using var sp = Build(new() { ["Nim:ApiKey"] = "nvapi-x" });

        Assert.NotNull(sp.GetRequiredService<IChatClient>());
        Assert.NotNull(sp.GetRequiredKeyedService<IChatClient>(ModelServiceCollectionExtensions.SmallChatClientKey));
        var embedder = sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
        Assert.Equal("embed-model", embedder.GetService<EmbeddingGeneratorMetadata>()!.DefaultModelId);
    }

    [Fact]
    public void Di_exposes_only_pii_masking_wrappers()
    {
        using var sp = Build(new() { ["Nim:ApiKey"] = "nvapi-x" });

        Assert.IsType<PiiMaskingChatClient>(sp.GetRequiredService<IChatClient>());
        Assert.IsType<PiiMaskingChatClient>(sp.GetRequiredKeyedService<IChatClient>(ModelServiceCollectionExtensions.SmallChatClientKey));
        Assert.IsType<PiiMaskingEmbeddingGenerator>(sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>());
    }

    [Fact]
    public void Missing_api_key_fails_with_a_clear_message()
    {
        using var sp = Build([]);

        var ex = Assert.Throws<InvalidOperationException>(() => sp.GetRequiredService<IChatClient>());
        Assert.Contains("NVIDIA_API_KEY", ex.Message, StringComparison.Ordinal);
    }

    private static ServiceProvider Build(Dictionary<string, string?> values)
    {
        values.TryAdd("Nim:ChatModel", "chat-model");
        values.TryAdd("Nim:SmallChatModel", "small-model");
        values.TryAdd("Nim:EmbeddingModel", "embed-model");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        return new ServiceCollection().AddNimModels(configuration).BuildServiceProvider();
    }
}
