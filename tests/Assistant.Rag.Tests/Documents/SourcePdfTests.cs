using System.Text.RegularExpressions;
using Assistant.Rag.Chunking;
using Assistant.Rag.Documents;

namespace Assistant.Rag.Tests.Documents;

/// <summary>
/// Reader + chunker on the real PDFs in docs/sources: three generated Selim Bank documents and the
/// official KVKK (Law No. 6698) PDF from mevzuat.gov.tr, which has a different, real-world layout.
/// </summary>
public partial class SourcePdfTests
{
    private static readonly ChunkingOptions Options = new();

    public static TheoryData<string> AllSources => ["kredi-karti-sozlesmesi", "ucret-tarifesi", "sss", "kvkk-6698"];

    [Theory]
    [InlineData("kredi-karti-sozlesmesi", "Selim Bank Bireysel Kredi Kartı Sözleşmesi", "Madde", 17, 18)]
    [InlineData("ucret-tarifesi", "Selim Bank Bireysel Ücret ve Komisyon Tarifesi", "Madde", 12, 13)]
    [InlineData("sss", "Selim Bank Sıkça Sorulan Sorular", "Soru", 15, 16)]
    public void Generated_documents_chunk_into_one_chunk_per_article_plus_intro(
        string docId, string title, string kind, int articles, int expectedChunks)
    {
        var chunks = Chunk(docId);

        Assert.Equal(expectedChunks, chunks.Count);
        Assert.All(chunks, c => Assert.Equal(title, c.DocTitle));
        Assert.Null(chunks[0].Label); // the "Kurgusal belgedir" intro
        Assert.Equal(Enumerable.Range(1, articles).Select(i => $"{kind} {i}"), chunks.Skip(1).Select(c => c.Label));
        Assert.All(chunks, c => Assert.Equal(1, c.PartCount));
    }

    [Fact]
    public void Contract_article_keeps_section_path_bullets_and_turkish_text()
    {
        var chunks = Chunk("kredi-karti-sozlesmesi");

        var madde2 = Assert.Single(chunks, c => c.Label == "Madde 2");
        Assert.Equal(["Bölüm 1 – Taraflar ve Tanımlar", "Madde 2 – Tanımlar"], madde2.SectionPath);
        Assert.Equal(4, madde2.Text.Split('\n').Count(l => l.StartsWith('•')));

        var madde8 = Assert.Single(chunks, c => c.Label == "Madde 8");
        Assert.Equal("Bölüm 3 – Kayıp, Çalıntı ve İtiraz", madde8.SectionPath[0]);
        Assert.Contains("zararın en çok 250 TL'sinden sorumludur", madde8.Text, StringComparison.Ordinal);
        Assert.Contains("ağır kusuru", madde8.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Kvkk_every_article_is_found_and_labelled()
    {
        var chunks = Chunk("kvkk-6698");
        var labels = chunks.Select(c => c.Label).Where(l => l is not null).Distinct().ToList();

        var expected = Enumerable.Range(1, 31).Select(i => $"Madde {i}")
            .Concat(Enumerable.Range(1, 3).Select(i => $"Geçici Madde {i}"))
            .Concat(["Madde 32", "Madde 33"]);
        Assert.Equal(expected, labels);
    }

    [Fact]
    public void Kvkk_article_text_starts_at_the_article_and_keeps_its_items()
    {
        var chunks = Chunk("kvkk-6698");

        var madde7 = chunks.First(c => c.Label == "Madde 7");
        Assert.StartsWith("MADDE 7-", madde7.Text, StringComparison.Ordinal);

        var madde11 = string.Join('\n', chunks.Where(c => c.Label == "Madde 11").Select(c => c.Text));
        Assert.Contains("İlgili kişinin hakları", chunks.First(c => c.Label == "Madde 11").SectionPath[^1], StringComparison.Ordinal);
        Assert.Contains("Kişisel veri işlenip işlenmediğini öğrenme", madde11, StringComparison.Ordinal);

        var madde3 = chunks.First(c => c.Label == "Madde 3");
        Assert.Contains("\na) Açık rıza:", madde3.Text, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(AllSources))]
    public void No_chunk_exceeds_the_token_limit_or_contains_two_article_starts(string docId)
    {
        foreach (var chunk in Chunk(docId))
        {
            Assert.True(StructureAwareChunker.EstimateTokens(chunk.Text) <= Options.MaxTokens, $"#{chunk.Index} too large");
            int articleStarts = chunk.Text.Split('\n').Count(line => ArticleStartRegex().IsMatch(line));
            Assert.True(articleStarts <= 1, $"#{chunk.Index} ({chunk.Label}) contains {articleStarts} article starts");
        }
    }

    private static IReadOnlyList<DocumentChunk> Chunk(string docId) =>
        new StructureAwareChunker(Options).Chunk(PdfDocumentReader.Read(Path.Combine(AppContext.BaseDirectory, "Sources", docId + ".pdf")));

    [GeneratedRegex(@"^(GEÇİCİ MADDE|MADDE|Madde|Soru) \d+\s*[-–]")]
    private static partial Regex ArticleStartRegex();
}
