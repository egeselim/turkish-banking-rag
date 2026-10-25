using Assistant.Rag.Search;

namespace Assistant.Rag.Tests.Search;

public class TurkishTextNormalizerTests
{
    [Fact]
    public void Lowercases_with_turkish_rules_folds_diacritics_and_stems()
    {
        Assert.Equal(["iptal", "karti", "ucret"], TurkishTextNormalizer.Terms("İPTAL Kartımı Ücretleri"));
    }

    [Fact]
    public void Query_without_turkish_characters_matches_the_proper_spelling()
    {
        Assert.Equal(
            TurkishTextNormalizer.Terms("Kartımı çaldırdım, ne yapmalıyım?"),
            TurkishTextNormalizer.Terms("kartimi caldirdim ne yapmaliyim"));
    }

    [Fact]
    public void Inflected_forms_share_a_stem()
    {
        string[] forms = ["ücret", "ücreti", "ücretleri", "ücretlerden"];

        Assert.Single(forms.SelectMany(TurkishTextNormalizer.Terms).Distinct());
    }

    [Fact]
    public void Drops_stop_words_and_single_letters_but_keeps_numbers_whole()
    {
        Assert.Equal(["eft", "ucret", "250", "50000"], TurkishTextNormalizer.Terms("EFT ücreti ne kadar ve 250 mi, 50000 mi? a"));
    }
}
