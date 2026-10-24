using System.ComponentModel;
using System.Globalization;
using System.Numerics.Tensors;
using Assistant.Core.Models;
using Assistant.Rag;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Assistant.Core.Tests.Live;

/// <summary>
/// Acceptance checks for step 1.2 against the models configured in Assistant.Web (ADR-001),
/// resolved through the real DI composition.
/// </summary>
[Trait("Category", "Live")]
public sealed class NimLiveTests : IDisposable
{
    private readonly ServiceProvider _services;

    public NimLiveTests()
    {
        LiveSettings.RequireApiKey();
        _services = new ServiceCollection().AddNimModels(LiveSettings.Configuration).BuildServiceProvider();
    }

    [Fact]
    public async Task Main_model_answers_in_turkish()
    {
        var client = _services.GetRequiredService<IChatClient>();

        var response = await client.GetResponseAsync(
            [
                new(ChatRole.System, "Sen bir banka asistanısın. Her zaman Türkçe ve kısa cevap ver."),
                new(ChatRole.User, "What is a credit card statement? One sentence."),
            ],
            new ChatOptions { MaxOutputTokens = 600 },
            TestContext.Current.CancellationToken);

        Assert.Contains(response.Text, c => "çğıöşüÇĞİÖŞÜ".Contains(c, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Main_model_calls_block_card_with_correct_json_arguments()
    {
        var client = _services.GetRequiredService<IChatClient>();

        var response = await client.GetResponseAsync(
            [new(ChatRole.User, "Sonu 4821 olan kartım çalındı, hemen kapatın.")],
            new ChatOptions { Tools = [AIFunctionFactory.Create(BlockCard)], MaxOutputTokens = 600 },
            TestContext.Current.CancellationToken);

        var call = Assert.Single(response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>());
        Assert.Equal(nameof(BlockCard), call.Name);
        Assert.Equal("4821", Convert.ToString(call.Arguments!["cardLast4"], CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Small_model_classifies_an_action_request()
    {
        var client = _services.GetRequiredKeyedService<IChatClient>(ModelServiceCollectionExtensions.SmallChatClientKey);

        var response = await client.GetResponseAsync(
            [
                new(ChatRole.System, "Mesajı 'bilgi' veya 'islem' olarak sınıflandır. YALNIZCA etiketi yaz."),
                new(ChatRole.User, "Bakiyemi göster"),
            ],
            new ChatOptions { MaxOutputTokens = 50, Temperature = 0f },
            TestContext.Current.CancellationToken);

        Assert.Equal("islem", response.Text.Trim().Trim('.').ToLowerInvariant());
    }

    [Fact]
    public async Task Embedding_ranks_the_matching_turkish_passage_first()
    {
        var generator = _services.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
        var ct = TestContext.Current.CancellationToken;

        var passages = await generator.GenerateAsync(
            [
                "EFT ücretleri: Mobil şubeden yapılan EFT işlemlerinden 6,50 TL ücret alınır.",
                "Kartın çalınması halinde kart hamili derhal müşteri hizmetlerini arayarak kartı kapattırır.",
            ],
            EmbeddingInputType.ForPassage(),
            ct);
        var query = await generator.GenerateAsync(["Kartımı çaldırdım ne yapayım"], EmbeddingInputType.ForQuery(), ct);

        float wrong = TensorPrimitives.CosineSimilarity(query[0].Vector.Span, passages[0].Vector.Span);
        float right = TensorPrimitives.CosineSimilarity(query[0].Vector.Span, passages[1].Vector.Span);
        Assert.True(right > wrong, $"Expected card-theft passage to score higher ({right:F3} vs {wrong:F3}).");
    }

    public void Dispose() => _services.Dispose();

    [Description("Müşterinin kartını kalıcı olarak kullanıma kapatır (bloke eder).")]
    private static string BlockCard([Description("Kartın son 4 hanesi.")] string cardLast4) => "{}";
}
