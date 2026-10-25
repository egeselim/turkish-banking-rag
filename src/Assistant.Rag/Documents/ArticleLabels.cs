using System.Globalization;
using System.Text.RegularExpressions;

namespace Assistant.Rag.Documents;

/// <summary>Recognises article/question starts: "MADDE 4-", "Madde 4 –", "GEÇİCİ MADDE 2-", "Soru 3 –".</summary>
internal static partial class ArticleLabels
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static bool StartsArticle(string text) => ArticleRegex().IsMatch(text);

    /// <summary>Normalised citation label such as "Madde 4", "Geçici Madde 2" or "Soru 3"; null if none.</summary>
    public static string? Label(string text)
    {
        var match = ArticleRegex().Match(text);
        if (!match.Success)
        {
            return null;
        }

        string kind = Regex.Replace(match.Groups["kind"].Value, @"\s+", " ");
        return $"{Turkish.TextInfo.ToTitleCase(Turkish.TextInfo.ToLower(kind))} {match.Groups["no"].Value}";
    }

    [GeneratedRegex(@"^(?<kind>GEÇİCİ\s+MADDE|Geçici\s+Madde|MADDE|Madde|Soru)\s+(?<no>\d+)\s*[-–—]")]
    private static partial Regex ArticleRegex();
}
