using System.Text;
using Assistant.Core.Safety;
using Assistant.Privacy;

namespace Assistant.Core.Tests.Safety;

public sealed class PiiStreamFilterTests
{
    private static readonly string Card = SyntheticPii.CardNumber();
    private static readonly string SpacedCard = string.Join(' ', Card.Chunk(4).Select(c => new string(c)));

    [Fact]
    public void Text_without_pii_comes_through_unchanged()
    {
        string text = string.Concat(Enumerable.Repeat("Bakiyeniz 1.250,00 TL olarak görünüyor. ", 10));

        Assert.Equal(text, Run(text.Chunk(7).Select(c => new string(c))));
    }

    [Fact]
    public void Card_number_in_one_part_is_masked()
    {
        Assert.Equal("Kart [KART_NO] burada.", Run([$"Kart {Card} burada."]));
    }

    [Fact]
    public void Card_number_split_across_parts_is_masked_at_every_split_point()
    {
        string text = $"Kart numaranız {SpacedCard} olarak kayıtlı.";
        for (int split = 1; split < text.Length; split++)
        {
            string result = Run([text[..split], text[split..]]);

            Assert.Equal("Kart numaranız [KART_NO] olarak kayıtlı.", result);
        }
    }

    [Fact]
    public void Card_number_arriving_character_by_character_never_leaks_in_any_emitted_part()
    {
        var filter = new PiiStreamFilter(new PiiMasker());
        var emitted = new StringBuilder();
        foreach (char c in $"{new string('x', 200)} {SpacedCard} {new string('y', 200)}")
        {
            emitted.Append(filter.Push(c.ToString()));
        }

        emitted.Append(filter.Flush());

        Assert.DoesNotContain(SpacedCard, emitted.ToString(), StringComparison.Ordinal);
        Assert.Contains("[KART_NO]", emitted.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Long_answers_keep_streaming_instead_of_waiting_for_the_end()
    {
        var filter = new PiiStreamFilter(new PiiMasker());

        string emitted = filter.Push(string.Concat(Enumerable.Repeat("kelime ", 50)));

        Assert.NotEmpty(emitted);
        Assert.Equal(string.Concat(Enumerable.Repeat("kelime ", 50)), emitted + filter.Flush());
    }

    private static string Run(IEnumerable<string> parts)
    {
        var filter = new PiiStreamFilter(new PiiMasker());
        var result = new StringBuilder();
        foreach (string part in parts)
        {
            result.Append(filter.Push(part));
        }

        return result.Append(filter.Flush()).ToString();
    }
}
