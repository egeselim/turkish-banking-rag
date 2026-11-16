using System.Text.RegularExpressions;

namespace Eval.Runner;

/// <summary>
/// Finds phone numbers in an answer that do not appear in the source documents. The 3.3 and 3.4 answer evals showed
/// the model inventing call-centre numbers ("444 0 444") after saying it found nothing; the documents contain none.
/// Turkish formats: call centres (444 x xxx), landlines and mobiles (0212 …, 0850 …, 05xx …), with or without +90.
/// Amounts ("1.650 TL"), dates ("23.10.2026"), times and card endings do not match.
/// </summary>
internal static partial class ContactCheck
{
    public static IReadOnlyList<string> InventedPhones(string answer, IReadOnlySet<string> knownPhones) =>
        [.. PhoneRegex().Matches(answer)
            .Select(m => Digits(m.Value))
            .Where(d => !knownPhones.Contains(d))
            .Distinct()];

    /// <summary>The phone numbers written in the sources, as digit strings, so quoting one is not counted as invented.</summary>
    public static IReadOnlySet<string> PhonesIn(IEnumerable<string> texts) =>
        texts.SelectMany(t => PhoneRegex().Matches(t).Select(m => Digits(m.Value))).ToHashSet(StringComparer.Ordinal);

    private static string Digits(string value)
    {
        string digits = string.Concat(value.Where(char.IsAsciiDigit));
        // "+90 212 …", "0212 …" and "212 …" are the same number.
        if (digits.StartsWith("90", StringComparison.Ordinal) && digits.Length == 12)
        {
            digits = digits[2..];
        }

        return digits.TrimStart('0');
    }

    [GeneratedRegex(@"(?<![\d.,:])(?:\+?90[\s-]?)?(?:444[\s-]?\d[\s-]?\d{3}|0?\(?[2-58]\d{2}\)?[\s-]?\d{3}[\s-]?\d{2}[\s-]?\d{2})(?![\d.,:])")]
    private static partial Regex PhoneRegex();
}
