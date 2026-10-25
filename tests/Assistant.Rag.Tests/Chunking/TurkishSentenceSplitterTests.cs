using Assistant.Rag.Chunking;

namespace Assistant.Rag.Tests.Chunking;

public class TurkishSentenceSplitterTests
{
    [Fact]
    public void Splits_on_sentence_punctuation_followed_by_whitespace()
    {
        var sentences = TurkishSentenceSplitter.Split("Kartınızı kapatın. Yeni kart gönderilir! Ücret alınır mı? Hayır.");

        Assert.Equal(["Kartınızı kapatın.", "Yeni kart gönderilir!", "Ücret alınır mı?", "Hayır."], sentences);
    }

    [Theory]
    [InlineData("Şubeden yapılan EFT işlemlerinden 1.250,75 TL alınır.")]
    [InlineData("Yürürlük tarihi 24.10.2026 olarak belirlenmiştir.")]
    [InlineData("Faiz oranı %4,25 olarak uygulanır.")]
    [InlineData("Kredi kartı, banka kartı vb. ürünler bu kapsamdadır.")]
    [InlineData("Selim Bank A.Ş. bu sözleşmenin tarafıdır.")]
    [InlineData("Ayrıntılar için bkz. Madde 9 ve Md. 10 hükümleri.")]
    [InlineData("1. Kartınızı mobil uygulamadan kapatın.")]
    public void Keeps_numbers_dates_abbreviations_and_list_markers_inside_one_sentence(string text) =>
        Assert.Equal([text], TurkishSentenceSplitter.Split(text));

    [Fact]
    public void Keeps_closing_quotes_with_the_sentence()
    {
        var sentences = TurkishSentenceSplitter.Split("Seçenek \"Kartı Kapat.\" Sonra onaylayın.");

        Assert.Equal(["Seçenek \"Kartı Kapat.\"", "Sonra onaylayın."], sentences);
    }

    [Fact]
    public void Returns_trailing_text_without_final_punctuation()
    {
        Assert.Equal(["Birinci cümle.", "son kısım"], TurkishSentenceSplitter.Split("Birinci cümle. son kısım"));
    }
}
