using System.Text;

namespace Assistant.Privacy;

public interface IPiiMasker
{
    /// <summary>Replaces every detected PII span with a typed placeholder such as <c>[TCKN]</c>.</summary>
    string Mask(string text);
}

/// <summary>
/// Masks PII with Turkish placeholders. The placeholders go into prompts, so the model can still
/// reason about them ("müşteri [KART_NO] numaralı kartı bildirdi").
/// </summary>
public sealed class PiiMasker : IPiiMasker
{
    public static string Placeholder(PiiType type) => type switch
    {
        PiiType.NationalId => "[TCKN]",
        PiiType.CardNumber => "[KART_NO]",
        PiiType.Iban => "[IBAN]",
        PiiType.Phone => "[TELEFON]",
        PiiType.Email => "[EPOSTA]",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    public string Mask(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var matches = PiiDetector.Detect(text);
        if (matches.Count == 0)
        {
            return text;
        }

        var sb = new StringBuilder(text.Length);
        int position = 0;
        foreach (var match in matches)
        {
            sb.Append(text, position, match.Start - position).Append(Placeholder(match.Type));
            position = match.End;
        }

        return sb.Append(text, position, text.Length - position).ToString();
    }
}
