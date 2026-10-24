using System.Text.RegularExpressions;
using Assistant.Privacy.Validation;

namespace Assistant.Privacy;

/// <summary>
/// Finds PII spans. Regexes only propose candidates; numeric identifiers must also pass their
/// checksum, so random 11-digit or 16-digit numbers are not treated as PII.
/// </summary>
public static partial class PiiDetector
{
    // When candidates overlap, the longer one wins; on equal length, the earlier type in this list.
    private static readonly PiiType[] Priority = [PiiType.Iban, PiiType.CardNumber, PiiType.NationalId, PiiType.Phone, PiiType.Email];

    public static IReadOnlyList<PiiMatch> Detect(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var candidates = new List<PiiMatch>();
        AddMatches(candidates, text, TcknRegex(), PiiType.NationalId, m => Checksums.IsValidTckn(m));
        AddMatches(candidates, text, CardRegex(), PiiType.CardNumber, m => IsValidCard(m));
        AddMatches(candidates, text, IbanRegex(), PiiType.Iban, m => Checksums.IsValidIban(Compact(m).ToUpperInvariant()));
        AddMatches(candidates, text, PhoneRegex(), PiiType.Phone, _ => true);
        AddMatches(candidates, text, EmailRegex(), PiiType.Email, _ => true);

        var accepted = new List<PiiMatch>();
        foreach (var candidate in candidates
                     .OrderByDescending(c => c.Length)
                     .ThenBy(c => Array.IndexOf(Priority, c.Type)))
        {
            if (!accepted.Exists(a => a.Overlaps(candidate)))
            {
                accepted.Add(candidate);
            }
        }

        accepted.Sort((a, b) => a.Start.CompareTo(b.Start));
        return accepted;
    }

    private static void AddMatches(List<PiiMatch> results, string text, Regex regex, PiiType type, Func<string, bool> isValid)
    {
        foreach (Match m in regex.Matches(text))
        {
            if (isValid(m.Value))
            {
                results.Add(new PiiMatch(type, m.Index, m.Length));
            }
        }
    }

    private static bool IsValidCard(string candidate)
    {
        string digits = Compact(candidate);
        return digits.Length is >= 13 and <= 19 && Checksums.IsValidLuhn(digits);
    }

    private static string Compact(string value) =>
        string.Concat(value.Where(char.IsAsciiLetterOrDigit));

    [GeneratedRegex(@"(?<!\d)\d{11}(?!\d)")]
    private static partial Regex TcknRegex();

    // Contiguous 13–19 digits, or the printed formats 4-4-4-4(-1..3) and 4-6-5 with one consistent separator.
    // Free-form digit runs are deliberately not matched: they would glue neighbouring numbers together.
    [GeneratedRegex(@"(?<![\d-])(?:\d{13,19}|\d{4}([ -])\d{4}\1\d{4}\1\d{4}(?:\1\d{1,3})?|\d{4}([ -])\d{6}\2\d{5})(?![\d-])")]
    private static partial Regex CardRegex();

    [GeneratedRegex(@"(?<![A-Za-z0-9])TR\d{2}(?: ?\d{4}){5} ?\d{2}(?!\d)", RegexOptions.IgnoreCase)]
    private static partial Regex IbanRegex();

    // Turkish mobile numbers: optional +90 / 0090 / 0 prefix, 5xx operator code, common separators.
    [GeneratedRegex(@"(?<![\d+])(?:(?:\+|00)90[ -]?|0[ -]?)?\(?5\d{2}\)?[ -]?\d{3}[ -]?\d{2}[ -]?\d{2}(?!\d)")]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"(?<![\w.%+-])[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}(?![\w])")]
    private static partial Regex EmailRegex();
}
