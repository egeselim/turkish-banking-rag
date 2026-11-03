using Assistant.Core.Chat;
using Assistant.Core.Tests.Safety;
using Assistant.Rag.Chunking;
using Assistant.Rag.Search;
using Microsoft.Extensions.AI;

namespace Assistant.Core.Tests.Chat;

public sealed class BankAssistantTests : IDisposable
{
    private static readonly DocumentChunk EftFees = new(
        Guid.NewGuid(), "ucret-tarifesi", "Selim Bank Bireysel Ücret ve Komisyon Tarifesi",
        ["Bölüm 2 – Para Transferleri", "Madde 4 – EFT Ücretleri"], "Madde 4", 1, 1, 4, 1, 1,
        "Mobil uygulama ve internet şubesi üzerinden yapılan EFT: 6,50 TL.");

    private static readonly DocumentChunk Rights = new(
        Guid.NewGuid(), "kvkk-6698", "KİŞİSEL VERİLERİN KORUNMASI KANUNU",
        ["ÜÇÜNCÜ BÖLÜM Haklar ve Yükümlülükler", "İlgili kişinin hakları"], "Madde 11", 5, 6, 20, 1, 1,
        "MADDE 11- (1) Herkes, veri sorumlusuna başvurarak kendisiyle ilgili; a) Kişisel veri işlenip işlenmediğini öğrenme...");

    private readonly PiiMaskingChatClientTests.RecordingChatClient _model = new();
    private readonly FakeRetriever _retriever = new([EftFees, Rights]);
    private readonly BankAssistant _assistant;

    public BankAssistantTests() => _assistant = new BankAssistant(_model, _retriever);

    [Fact]
    public async Task Model_receives_the_grounding_rules_and_numbered_sources()
    {
        var turn = await AskAsync("EFT ücreti ne kadar?");

        Assert.Equal(["EFT ücreti ne kadar?"], _retriever.Queries);
        Assert.Equal([1, 2], turn.Sources.Select(s => s.Number));
        string instructions = _model.Options!.Instructions!;
        Assert.Contains("Yalnızca KAYNAKLAR bölümündeki bilgilere dayanarak", instructions, StringComparison.Ordinal);
        Assert.Contains("[1] Selim Bank Bireysel Ücret ve Komisyon Tarifesi · Madde 4 – EFT Ücretleri · s. 1", instructions, StringComparison.Ordinal);
        Assert.Contains("6,50 TL", instructions, StringComparison.Ordinal);
        Assert.Contains("[2] KİŞİSEL VERİLERİN KORUNMASI KANUNU · Madde 11 – İlgili kişinin hakları · s. 5–6", instructions, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Follow_up_sees_the_earlier_question_but_not_earlier_sources()
    {
        var session = await _assistant.CreateSessionAsync(TestContext.Current.CancellationToken);
        await DrainAsync(await _assistant.AskAsync(session, "EFT ücreti ne kadar?", TestContext.Current.CancellationToken));
        await DrainAsync(await _assistant.AskAsync(session, "Peki şubeden?", TestContext.Current.CancellationToken));

        string history = string.Join('\n', _model.Messages.Select(m => m.Text));
        Assert.Contains("EFT ücreti ne kadar?", history, StringComparison.Ordinal);
        Assert.Contains("Peki şubeden?", history, StringComparison.Ordinal);
        Assert.DoesNotContain("<<<", history, StringComparison.Ordinal); // sources live in per-turn instructions only
    }

    [Fact]
    public async Task No_sources_tells_the_model_so()
    {
        _retriever.Results = [];

        var turn = await AskAsync("Genel müdür kim?");

        Assert.Empty(turn.Sources);
        Assert.Contains("ilgili kaynak bulunamadı", _model.Options!.Instructions!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Streams_the_model_answer()
    {
        var turn = await _assistant.AskAsync(
            await _assistant.CreateSessionAsync(TestContext.Current.CancellationToken), "EFT?", TestContext.Current.CancellationToken);

        Assert.Equal("tamam", await DrainAsync(turn));
    }

    public void Dispose() => _model.Dispose();

    private async Task<AssistantTurn> AskAsync(string question)
    {
        var session = await _assistant.CreateSessionAsync(TestContext.Current.CancellationToken);
        var turn = await _assistant.AskAsync(session, question, TestContext.Current.CancellationToken);
        await DrainAsync(turn);
        return turn;
    }

    private static async Task<string> DrainAsync(AssistantTurn turn)
    {
        var text = new System.Text.StringBuilder();
        await foreach (string part in turn.Text)
        {
            text.Append(part);
        }

        return text.ToString();
    }

    private sealed class FakeRetriever(IReadOnlyList<DocumentChunk> results) : IDocumentRetriever
    {
        public IReadOnlyList<DocumentChunk> Results { get; set; } = results;

        public List<string> Queries { get; } = [];

        public Task<IReadOnlyList<RetrievedChunk>> SearchAsync(
            string query, int top = 5, RetrievalMode mode = RetrievalMode.Hybrid, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            return Task.FromResult<IReadOnlyList<RetrievedChunk>>([.. Results.Take(top).Select((c, i) => new RetrievedChunk(c, 1.0 / (i + 1), i + 1, null))]);
        }
    }
}
