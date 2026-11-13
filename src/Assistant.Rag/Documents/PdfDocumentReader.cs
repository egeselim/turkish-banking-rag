using System.Globalization;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Assistant.Rag.Documents;

/// <summary>
/// Recovers headings and paragraphs from a PDF using layout only (font size, weight, position).
/// Tuned for two layouts: generated Selim Bank documents (headings by size, paragraphs by spacing)
/// and Turkish legislation from mevzuat.gov.tr (bold headings, paragraphs by first-line indent).
/// </summary>
/// <remarks>Known limits: tables are read as plain lines; repeated page headers/footers are not removed.</remarks>
public static partial class PdfDocumentReader
{
    private const char Bullet = '•';

    // "(2) ...", "a) ...", "ç) ...", "1. ..." — paragraph and item markers. Legislation prints some of these
    // in bold (amendment notes), but they continue the current article and must never become headings.
    [System.Text.RegularExpressions.GeneratedRegex(@"^(\(\d+\)|\p{Ll}\)|\d+\.)\s")]
    private static partial System.Text.RegularExpressions.Regex ItemMarkerRegex();

    // Centered is set in BuildBlocks, once the page's text margin is known (see IsCentered).
    private sealed record Line(int Page, double PageWidth, double Top, double Bottom, double Left, double Size, bool Bold, string Text, bool Centered = false);

    public static SourceDocument Read(string path) =>
        Read(File.OpenRead(path), Path.GetFileNameWithoutExtension(path), leaveOpen: false);

    public static SourceDocument Read(Stream pdf, string docId, bool leaveOpen = true)
    {
        try
        {
            using var document = PdfDocument.Open(pdf);
            // Lines that are only digits are page numbers or footnote markers.
            var lines = document.GetPages().SelectMany(ReadLines).Where(l => !l.Text.All(char.IsAsciiDigit)).ToList();
            var blocks = BuildBlocks(lines);
            string title = blocks.FirstOrDefault(b => b.Kind == BlockKind.Heading)?.Text ?? docId;
            return new SourceDocument(docId, title, blocks);
        }
        finally
        {
            if (!leaveOpen)
            {
                pdf.Dispose();
            }
        }
    }

    private static IEnumerable<Line> ReadLines(Page page) =>
        ClusterIntoLines(page.GetWords().Where(w => w.Letters.Count > 0))
            .Select(cluster =>
            {
                var words = cluster.OrderBy(w => w.BoundingBox.Left).ToList();
                var letters = words.SelectMany(w => w.Letters).ToList();
                double size = letters.GroupBy(l => Math.Round(l.PointSize, 1)).MaxBy(s => s.Count())!.Key;
                bool bold = letters.Count(l => IsBold(l.FontName)) >= letters.Count * 0.8;
                return new Line(page.Number, page.Width, words.Max(w => w.BoundingBox.Top), words.Min(w => w.BoundingBox.Bottom),
                    words[0].BoundingBox.Left, size, bold, string.Join(' ', words.Select(w => w.Text)));
            });

    /// <summary>
    /// A heading is "centered" when it starts well inside the page's text margin. Only the left edge is used: it is
    /// written in the PDF, whereas the right edge depends on glyph widths, which PdfPig takes from installed system
    /// fonts when a font is not embedded (the KVKK PDF's Times New Roman). Measuring the right edge made the same
    /// heading centered on Linux and left-aligned on Windows (2026-11-13, CI), changing chunk sections.
    /// The 10%-of-page step is wider than a first-line indent (KVKK article titles: ~35 pt) and narrower
    /// than any centered section title in the sources (≥ 64 pt).
    /// </summary>
    private static bool IsCentered(Line line, double margin) =>
        line.Left > line.PageWidth * 0.15 && line.Left - margin > line.PageWidth * 0.1;

    // Words whose baselines are within 40% of the font size belong to one visual line. Fixed-size
    // buckets would split e.g. a bold "MADDE 7-" from the rest of its line when the baselines differ slightly.
    private static List<List<Word>> ClusterIntoLines(IEnumerable<Word> words)
    {
        var lines = new List<List<Word>>();
        double lineBaseline = double.NaN;

        foreach (var word in words.OrderByDescending(w => w.Letters[0].StartBaseLine.Y).ThenBy(w => w.BoundingBox.Left))
        {
            double baseline = word.Letters[0].StartBaseLine.Y;
            double tolerance = word.Letters[0].PointSize * 0.4;
            if (lines.Count == 0 || Math.Abs(lineBaseline - baseline) > tolerance)
            {
                lines.Add([]);
                lineBaseline = baseline;
            }

            lines[^1].Add(word);
        }

        return lines;
    }

    private static bool IsBold(string? fontName) =>
        fontName is not null && fontName.Contains("Bold", StringComparison.OrdinalIgnoreCase);

    private static List<DocumentBlock> BuildBlocks(List<Line> lines)
    {
        if (lines.Count == 0)
        {
            return [];
        }

        double bodySize = lines.GroupBy(l => l.Size).MaxBy(g => g.Sum(l => l.Text.Length))!.Key;
        bool IsHeading(Line l)
        {
            // Any line set noticeably larger than body text is a heading (11pt over 10pt body counts).
            if (l.Size > bodySize + 0.5)
            {
                return true;
            }

            // Body-size bold lines are headings only if they look like one: not an item marker,
            // not an article start ("MADDE 7- (Değişik...)" is bold in legislation) and not lower-case
            // continuation text of a bold amendment note.
            return l.Bold
                && !ItemMarkerRegex().IsMatch(l.Text)
                && !ArticleLabels.StartsArticle(l.Text)
                && !char.IsLower(l.Text[0]);
        }

        var bodyLines = lines.Where(l => !IsHeading(l)).ToList();
        double lineSpacing = MedianLineSpacing(bodyLines, bodySize);

        // The text margin is the leftmost x that recurs on the page. Not the most frequent x:
        // in legislation, indented first lines can outnumber the continuation lines at the margin.
        var baseMargin = bodyLines.GroupBy(l => l.Page).ToDictionary(
            g => g.Key,
            g => g.GroupBy(l => Math.Round(l.Left)).Where(x => x.Count() >= 2).Select(x => x.Key).DefaultIfEmpty(g.Min(l => l.Left)).Min());

        // Pages without body text (a title page) fall back to their leftmost line as the margin.
        var pageMinLeft = lines.GroupBy(l => l.Page).ToDictionary(g => g.Key, g => g.Min(l => l.Left));
        lines = [.. lines.Select(l => l with { Centered = IsCentered(l, baseMargin.GetValueOrDefault(l.Page, pageMinLeft[l.Page])) })];

        // Heading levels: larger font first; at equal size, centered headings rank above left-aligned ones.
        var headingStyles = lines.Where(IsHeading)
            .Select(l => (l.Size, l.Centered))
            .Distinct()
            .OrderByDescending(s => s.Size)
            .ThenByDescending(s => s.Centered)
            .ToList();

        var blocks = new List<DocumentBlock>();
        var text = new StringBuilder();
        Line? first = null, previous = null;
        bool currentIsHeading = false;

        void Flush()
        {
            if (first is null)
            {
                return;
            }

            string value = text.ToString().Trim();
            if (value.Length > 0)
            {
                blocks.Add(currentIsHeading
                    ? new DocumentBlock(BlockKind.Heading, value, first.Page, previous!.Page, headingStyles.IndexOf((first.Size, first.Centered)) + 1)
                    : new DocumentBlock(BlockKind.Paragraph, value, first.Page, previous!.Page));
            }

            text.Clear();
            first = null;
        }

        foreach (var line in lines)
        {
            bool heading = IsHeading(line);
            bool startsNew = first is null
                || heading != currentIsHeading
                || (heading && (line.Size, line.Centered) != (first.Size, first.Centered))
                || IsBreak(line, previous!, first, heading, lineSpacing, baseMargin);

            if (startsNew)
            {
                Flush();
                first = line;
                currentIsHeading = heading;
            }
            else
            {
                AppendContinuation(text);
            }

            text.Append(line.Text);
            previous = line;
        }

        Flush();
        return blocks;
    }

    private static bool IsBreak(Line line, Line previous, Line first, bool heading, double lineSpacing, Dictionary<int, double> baseMargin)
    {
        if (line.Page != previous.Page)
        {
            // A paragraph continues onto the next page only if it was cut mid-sentence
            // and the new page starts at the margin (not with an indent or bullet).
            bool previousEnded = EndsSentence(previous.Text);
            bool indented = line.Left > baseMargin.GetValueOrDefault(line.Page, line.Left) + 10;
            return heading || previousEnded || indented || line.Text[0] == Bullet;
        }

        double gap = previous.Bottom - line.Bottom;
        if (gap > lineSpacing * 1.35)
        {
            return true;
        }

        if (heading)
        {
            return false;
        }

        if (line.Text[0] == Bullet)
        {
            return true;
        }

        // First-line indent (legislation layout). Inside a bullet item, indented lines are continuations.
        bool inBulletItem = first.Text[0] == Bullet;
        return !inBulletItem && line.Left > baseMargin.GetValueOrDefault(line.Page, line.Left) + 10;
    }

    private static void AppendContinuation(StringBuilder text)
    {
        // Join words hyphenated across lines ("ver-" + "ilir"), otherwise join with a space.
        if (text.Length > 1 && text[^1] == '-' && char.IsLetter(text[^2]))
        {
            text.Length--;
        }
        else
        {
            text.Append(' ');
        }
    }

    private static bool EndsSentence(string text) => text.Length > 0 && ".:;!?".Contains(text[^1], StringComparison.Ordinal);

    private static double MedianLineSpacing(List<Line> bodyLines, double bodySize)
    {
        var gaps = bodyLines.Zip(bodyLines.Skip(1))
            .Where(p => p.First.Page == p.Second.Page)
            .Select(p => p.First.Bottom - p.Second.Bottom)
            .Where(g => g > 0)
            .Order()
            .ToList();

        return gaps.Count == 0 ? bodySize * 1.4 : gaps[gaps.Count / 2];
    }

    internal static string Describe(SourceDocument document) =>
        string.Join('\n', document.Blocks.Select(b => string.Create(CultureInfo.InvariantCulture,
            $"[{b.Kind}{(b.Level > 0 ? b.Level.ToString(CultureInfo.InvariantCulture) : "")} p{b.PageStart}] {b.Text}")));
}
