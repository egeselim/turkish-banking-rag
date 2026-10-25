using Assistant.Rag.Search;

namespace Assistant.Rag.Tests.Search;

public class Bm25IndexTests
{
    private static readonly Guid Eft = Guid.NewGuid();
    private static readonly Guid Card = Guid.NewGuid();
    private static readonly Guid Fee = Guid.NewGuid();

    private readonly Bm25Index _index = new([
        (Eft, "EFT ücretleri: mobil şubeden yapılan EFT işlemlerinden 6,50 TL alınır."),
        (Card, "Kartın çalınması hâlinde kart hamili kartı derhal kullanıma kapattırır."),
        (Fee, "Kredi kartı yıllık üyelik ücreti kart türüne göre belirlenir."),
    ]);

    [Fact]
    public void Ranks_the_document_with_the_rare_matching_terms_first()
    {
        var hits = _index.Search("Mobilden EFT yapınca ne kadar ücret alınıyor?", top: 3);

        Assert.Equal(Eft, hits[0].Id);
    }

    [Fact]
    public void Matches_inflected_and_unaccented_query_terms()
    {
        var hits = _index.Search("kartimi caldirdim", top: 3);

        Assert.Equal(Card, hits[0].Id);
    }

    [Fact]
    public void Returns_nothing_when_no_term_matches()
    {
        Assert.Empty(_index.Search("hava durumu yarın", top: 3));
    }

    [Fact]
    public void Respects_top()
    {
        Assert.Single(_index.Search("kart ücret", top: 1));
    }
}
