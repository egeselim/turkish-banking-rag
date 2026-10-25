using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;

namespace SourceDocs.Generator;

internal static class PdfRenderer
{
    private const double Left = 56;
    private const double Right = 539; // A4 width 595 - 56
    private const double Top = 786;
    private const double Bottom = 60;
    private const double BulletIndent = 14;

    private sealed record Style(double Size, bool Bold, double SpaceBefore, double SpaceAfter);

    private static readonly Style Title = new(16, true, 0, 10);
    private static readonly Style Section = new(13, true, 14, 6);
    private static readonly Style Heading = new(11, true, 10, 4);
    private static readonly Style Body = new(10, false, 0, 7);

    public static byte[] Render(IEnumerable<string> markdownLines, string fontDir)
    {
        using var builder = new PdfDocumentBuilder();
        var regular = builder.AddTrueTypeFont(File.ReadAllBytes(Path.Combine(fontDir, "DejaVuSans.ttf")));
        var bold = builder.AddTrueTypeFont(File.ReadAllBytes(Path.Combine(fontDir, "DejaVuSans-Bold.ttf")));

        var page = builder.AddPage(PageSize.A4);
        double y = Top;

        foreach (var (style, text, bullet) in Parse(markdownLines))
        {
            var font = style.Bold ? bold : regular;
            double lineHeight = style.Size * 1.4;
            double indent = bullet ? BulletIndent : 0;
            var lines = Wrap(page, text, style.Size, font, Right - Left - indent);

            // Keep a heading together with at least three lines of what follows it.
            double needed = (style.SpaceBefore + (lines.Count * lineHeight)) + (style.Bold ? 3 * Body.Size * 1.4 : 0);
            if (y - needed < Bottom)
            {
                page = builder.AddPage(PageSize.A4);
                y = Top;
            }
            else
            {
                y -= style.SpaceBefore;
            }

            for (int i = 0; i < lines.Count; i++)
            {
                if (y - lineHeight < Bottom)
                {
                    page = builder.AddPage(PageSize.A4);
                    y = Top;
                }

                y -= lineHeight;
                if (bullet && i == 0)
                {
                    page.AddText("•", style.Size, new PdfPoint(Left + 2, y), font);
                }

                page.AddText(lines[i], style.Size, new PdfPoint(Left + indent, y), font);
            }

            y -= style.SpaceAfter;
        }

        return builder.Build();
    }

    private static IEnumerable<(Style Style, string Text, bool Bullet)> Parse(IEnumerable<string> lines)
    {
        var paragraph = new List<string>();

        foreach (string raw in lines.Append(""))
        {
            string line = raw.TrimEnd();
            bool isBlock = line.StartsWith('#') || line.StartsWith("- ", StringComparison.Ordinal);

            if ((line.Length == 0 || isBlock) && paragraph.Count > 0)
            {
                yield return (Body, string.Join(' ', paragraph), false);
                paragraph.Clear();
            }

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                yield return (Heading, line[4..], false);
            }
            else if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                yield return (Section, line[3..], false);
            }
            else if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                yield return (Title, line[2..], false);
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                yield return (Body with { SpaceAfter = 3 }, line[2..], true);
            }
            else if (line.Length > 0)
            {
                paragraph.Add(line);
            }
        }
    }

    private static List<string> Wrap(PdfPageBuilder page, string text, double size, PdfDocumentBuilder.AddedFont font, double width)
    {
        var lines = new List<string>();
        string current = "";

        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = current.Length == 0 ? word : current + " " + word;
            if (current.Length > 0 && Measure(page, candidate, size, font) > width)
            {
                lines.Add(current);
                current = word;
            }
            else
            {
                current = candidate;
            }
        }

        if (current.Length > 0)
        {
            lines.Add(current);
        }

        return lines;
    }

    private static double Measure(PdfPageBuilder page, string text, double size, PdfDocumentBuilder.AddedFont font)
    {
        var letters = page.MeasureText(text, size, new PdfPoint(0, 0), font);
        return letters.Count == 0 ? 0 : letters[^1].EndBaseLine.X;
    }
}
