using System.Globalization;
using System.Text.RegularExpressions;

namespace Assistant.Core.Chat;

public static partial class Citations
{
    /// <summary>The sources an answer actually cites ("[2]", "[1][3]"), in source order; unknown numbers are ignored.</summary>
    public static IReadOnlyList<CitedSource> UsedIn(string answer, IReadOnlyList<CitedSource> sources)
    {
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(sources);

        var cited = CitationRegex().Matches(answer)
            .Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToHashSet();

        return [.. sources.Where(s => cited.Contains(s.Number))];
    }

    [GeneratedRegex(@"\[(\d{1,2})\]")]
    private static partial Regex CitationRegex();
}
