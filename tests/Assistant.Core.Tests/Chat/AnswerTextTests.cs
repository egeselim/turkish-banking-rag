using Assistant.Core.Chat;

namespace Assistant.Core.Tests.Chat;

public class AnswerTextTests
{
    [Theory]
    [InlineData("EFT **6,50 TL** maliyetlidir [1].", "EFT 6,50 TL maliyetlidir [1].")]
    [InlineData("## Ücretler\n- Mobil: 6,50 TL\n- Şube: 32 TL", "Ücretler\n- Mobil: 6,50 TL\n- Şube: 32 TL")]
    [InlineData("__önemli__ not", "önemli not")]
    [InlineData("Madde #4 ve %4,25 faiz", "Madde #4 ve %4,25 faiz")]
    public void Strips_markdown_emphasis_and_headings_but_keeps_text(string input, string expected) =>
        Assert.Equal(expected, AnswerText.ToPlain(input));
}
