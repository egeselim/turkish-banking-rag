using Assistant.Core.Chat;

namespace Assistant.Core.Tests.Chat;

public class CitationsTests
{
    private static readonly CitedSource[] Sources = [Source(1), Source(2), Source(3)];

    [Fact]
    public void Returns_only_cited_sources_in_source_order()
    {
        var used = Citations.UsedIn("Mobil EFT 6,50 TL'dir [3]. Şubeden 32 TL [1][3].", Sources);

        Assert.Equal([1, 3], used.Select(s => s.Number));
    }

    [Fact]
    public void Ignores_numbers_without_a_source_and_answers_without_citations()
    {
        Assert.Empty(Citations.UsedIn("Bu konuda dokümanlarımızda bilgi bulamadım. [7]", Sources));
        Assert.Empty(Citations.UsedIn("Kurallar 2026 yılında değişti.", Sources));
    }

    [Theory]
    [InlineData("Madde 8", new[] { "Bölüm 3", "Madde 8 – Sorumluluk" }, "Madde 8 – Sorumluluk")]
    [InlineData("Madde 11", new[] { "ÜÇÜNCÜ BÖLÜM", "İlgili kişinin hakları" }, "Madde 11 – İlgili kişinin hakları")]
    [InlineData(null, new[] { "Kartlar" }, "Kartlar")]
    [InlineData("Madde 2", new string[0], "Madde 2")]
    public void Location_combines_label_and_heading_without_repeating(string? label, string[] path, string expected) =>
        Assert.Equal(expected, (Source(1) with { Label = label, SectionPath = path }).Location);

    private static CitedSource Source(int n) => new(n, "doc", "Doküman", $"Madde {n}", [], 1, 1, "metin");
}
