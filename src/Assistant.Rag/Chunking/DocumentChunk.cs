namespace Assistant.Rag.Chunking;

/// <summary>
/// A retrievable piece of a source document. One chunk never spans two articles/questions.
/// </summary>
/// <param name="Id">Deterministic: same document, position and text give the same id.</param>
/// <param name="SectionPath">Headings above the chunk, outermost first, without the document title.</param>
/// <param name="Label">Citation label such as "Madde 4", "Geçici Madde 1" or "Soru 3"; null when none applies.</param>
/// <param name="Part">1-based part number when a long article was split; <paramref name="PartCount"/> is the total.</param>
public sealed record DocumentChunk(
    Guid Id,
    string DocId,
    string DocTitle,
    IReadOnlyList<string> SectionPath,
    string? Label,
    int PageStart,
    int PageEnd,
    int Index,
    int Part,
    int PartCount,
    string Text)
{
    /// <summary>
    /// Text that is embedded and keyword-indexed: the location prefix lets "EFT ücreti" match an
    /// article whose body only says "aşağıdaki ücretler uygulanır".
    /// </summary>
    public string ContextualText => $"{string.Join(" > ", SectionPath.Prepend(DocTitle))}\n{Text}";
}
