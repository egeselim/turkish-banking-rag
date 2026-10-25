using Assistant.Rag.Chunking;
using Assistant.Rag.Documents;

namespace Assistant.Rag.Tests.Chunking;

public class StructureAwareChunkerTests
{
    private const string Title = "Örnek Sözleşme";

    [Fact]
    public void Each_article_becomes_its_own_chunk_with_label_path_and_pages()
    {
        var doc = Doc(
            H(1, Title),
            P("Kurgusal belgedir."),
            H(2, "Bölüm 1 – Genel"),
            H(3, "Madde 1 – Amaç"),
            P("Bu sözleşmenin amacı kart kullanımını düzenlemektir.", page: 1),
            H(3, "Madde 2 – Tanımlar"),
            P("Aşağıdaki tanımlar geçerlidir:", page: 1),
            P("• Limit: harcama üst sınırı.", page: 2));

        var chunks = new StructureAwareChunker().Chunk(doc);

        Assert.Equal(3, chunks.Count);
        Assert.Null(chunks[0].Label);
        Assert.Empty(chunks[0].SectionPath);

        Assert.Equal("Madde 2", chunks[2].Label);
        Assert.Equal(["Bölüm 1 – Genel", "Madde 2 – Tanımlar"], chunks[2].SectionPath);
        Assert.Equal("Aşağıdaki tanımlar geçerlidir:\n• Limit: harcama üst sınırı.", chunks[2].Text);
        Assert.Equal((1, 2), (chunks[2].PageStart, chunks[2].PageEnd));
        Assert.Equal(
            "Örnek Sözleşme > Bölüm 1 – Genel > Madde 2 – Tanımlar\nAşağıdaki tanımlar geçerlidir:\n• Limit: harcama üst sınırı.",
            chunks[2].ContextualText);
    }

    [Fact]
    public void Legislation_style_article_start_in_a_paragraph_opens_a_new_unit()
    {
        var doc = Doc(
            H(1, "KANUN"),
            H(2, "Amaç"),
            P("MADDE 1- (1) Bu Kanunun amacı korumaktır."),
            P("MADDE 2- (1) Bu Kanun herkese uygulanır."),
            P("(2) İstisnalar saklıdır."),
            P("GEÇİCİ MADDE 1- (1) Geçiş hükmü."));

        var chunks = new StructureAwareChunker().Chunk(doc);

        Assert.Equal(["Madde 1", "Madde 2", "Geçici Madde 1"], chunks.Select(c => c.Label));
        Assert.Equal("MADDE 2- (1) Bu Kanun herkese uygulanır.\n(2) İstisnalar saklıdır.", chunks[1].Text);
    }

    [Fact]
    public void Long_article_is_split_at_sentences_with_overlap_and_never_mixes_articles()
    {
        string sentence = "Kart hamili şifresini gizli tutmak ve üçüncü kişilerle paylaşmamakla yükümlüdür.";
        string longParagraph = string.Join(' ', Enumerable.Repeat(sentence, 20));
        var doc = Doc(H(1, Title), H(3, "Madde 5 – Güvenlik"), P(longParagraph), H(3, "Madde 6 – Diğer"), P("Kısa madde."));
        var options = new ChunkingOptions { MaxTokens = 120, TargetTokens = 100, OverlapTokens = 30 };

        var chunks = new StructureAwareChunker(options).Chunk(doc);

        var article5 = chunks.Where(c => c.Label == "Madde 5").ToList();
        Assert.True(article5.Count > 1);
        Assert.All(article5, c => Assert.True(StructureAwareChunker.EstimateTokens(c.Text) <= options.MaxTokens));
        Assert.All(article5, c => Assert.Equal(article5.Count, c.PartCount));
        Assert.Equal(Enumerable.Range(1, article5.Count), article5.Select(c => c.Part));
        Assert.EndsWith(sentence, article5[0].Text, StringComparison.Ordinal);
        Assert.StartsWith(sentence, article5[1].Text, StringComparison.Ordinal); // overlap carried over
        Assert.All(article5, c => Assert.DoesNotContain("Kısa madde", c.Text, StringComparison.Ordinal));
        Assert.Equal("Kısa madde.", Assert.Single(chunks, c => c.Label == "Madde 6").Text);
    }

    [Fact]
    public void Chunk_ids_are_deterministic_and_unique()
    {
        var doc = Doc(H(1, Title), H(3, "Madde 1 – A"), P("Birinci."), H(3, "Madde 2 – B"), P("İkinci."));

        var first = new StructureAwareChunker().Chunk(doc).Select(c => c.Id).ToList();
        var second = new StructureAwareChunker().Chunk(doc).Select(c => c.Id).ToList();

        Assert.Equal(first, second);
        Assert.Equal(first.Count, first.Distinct().Count());
    }

    [Fact]
    public void Headings_without_text_produce_no_chunks()
    {
        var doc = Doc(H(1, Title), H(2, "Bölüm 1"), H(2, "Bölüm 2"), H(3, "Madde 1 – A"), P("Metin."));

        var chunk = Assert.Single(new StructureAwareChunker().Chunk(doc));
        Assert.Equal(["Bölüm 2", "Madde 1 – A"], chunk.SectionPath);
    }

    private static SourceDocument Doc(params DocumentBlock[] blocks) => new("ornek", Title, blocks);

    private static DocumentBlock H(int level, string text) => new(BlockKind.Heading, text, 1, 1, level);

    private static DocumentBlock P(string text, int page = 1) => new(BlockKind.Paragraph, text, page, page);
}
