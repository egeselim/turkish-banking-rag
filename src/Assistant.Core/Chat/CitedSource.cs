using System.Globalization;
using Assistant.Rag.Chunking;

namespace Assistant.Core.Chat;

/// <summary>A source passage given to the model for one turn, numbered as the model cites it ([1], [2], ...).</summary>
public sealed record CitedSource(
    int Number,
    string DocId,
    string DocTitle,
    string? Label,
    IReadOnlyList<string> SectionPath,
    int PageStart,
    int PageEnd,
    string Text)
{
    public static CitedSource From(int number, DocumentChunk chunk) => new(
        number, chunk.DocId, chunk.DocTitle, chunk.Label, chunk.SectionPath, chunk.PageStart, chunk.PageEnd, chunk.Text);

    /// <summary>"Madde 8 – Bildirim Öncesi ve Sonrası Sorumluluk", "Madde 11 – İlgili kişinin hakları" or the section heading.</summary>
    public string Location
    {
        get
        {
            string? heading = SectionPath.Count > 0 ? SectionPath[^1] : null;
            if (Label is null)
            {
                return heading ?? "";
            }

            return heading is null || heading.StartsWith(Label, StringComparison.Ordinal) ? heading ?? Label : $"{Label} – {heading}";
        }
    }

    public string Pages => PageStart == PageEnd
        ? string.Create(CultureInfo.InvariantCulture, $"s. {PageStart}")
        : string.Create(CultureInfo.InvariantCulture, $"s. {PageStart}–{PageEnd}");
}
