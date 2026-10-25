using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Assistant.Rag.Search;

/// <summary>
/// Turns Turkish text into keyword-search terms, applied identically to documents and queries:
/// tr-TR lower-casing (İ→i, I→ı), diacritic folding (ı→i, ş→s, ç→c, ğ→g, ö→o, ü→u, â→a) so that
/// queries typed without Turkish characters still match, stop-word removal, and prefix stemming.
/// </summary>
/// <remarks>
/// Prefix stemming keeps the first <see cref="StemLength"/> characters ("kartımı" → "karti",
/// "ücretleri" → "ucret"). Turkish IR studies report that 5-character truncation performs close to
/// a full morphological stemmer. Words of up to 5 characters are kept whole.
/// </remarks>
public static partial class TurkishTextNormalizer
{
    public const int StemLength = 5;

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    // Folded forms. Question words and pronouns carry no topic.
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "ve", "veya", "ile", "ya", "da", "de", "ki", "mi", "mu", "bir", "bu", "su", "o", "ne", "neden", "nasil",
        "icin", "gibi", "ise", "olan", "olarak", "her", "hangi", "kac", "midir", "mudur", "var", "yok", "en",
        "cok", "daha", "ben", "beni", "bana", "benim", "sen", "siz", "size", "sizin", "biz", "ama", "fakat",
        "acaba", "sey", "kadar", "nedir", "ederim", "eder", "yapmaliyim", "istiyorum", "lutfen",
    };

    /// <summary>Normalised, stemmed terms in text order (duplicates kept, for term frequency).</summary>
    public static IReadOnlyList<string> Terms(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var terms = new List<string>();
        foreach (Match match in WordRegex().Matches(text))
        {
            string folded = Fold(match.Value.ToLower(Turkish));
            if (folded.Length < 2 || StopWords.Contains(folded))
            {
                continue;
            }

            terms.Add(folded.Length > StemLength && !folded.All(char.IsAsciiDigit) ? folded[..StemLength] : folded);
        }

        return terms;
    }

    private static string Fold(string lower)
    {
        var sb = new StringBuilder(lower.Length);
        foreach (char c in lower)
        {
            sb.Append(c switch
            {
                'ı' => 'i',
                'ş' => 's',
                'ç' => 'c',
                'ğ' => 'g',
                'ö' => 'o',
                'ü' => 'u',
                'â' => 'a',
                'î' => 'i',
                'û' => 'u',
                _ => c,
            });
        }

        return sb.ToString();
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex WordRegex();
}
