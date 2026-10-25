// Renders the fictional Selim Bank source documents (docs/sources/src/*.md) to PDF.
// Usage: dotnet run --project tools/SourceDocs.Generator -- <source dir> <output dir>
//
// Supported markdown subset: "# " title, "## " section, "### " article/question heading,
// "- " bullet, blank-line separated paragraphs. Headings are bold and larger, like real bank PDFs,
// so the ingestion reader has to recover structure from font size and weight.
using SourceDocs.Generator;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: SourceDocs.Generator <source dir> <output dir>");
    return 1;
}

string fontDir = Path.Combine(AppContext.BaseDirectory, "Fonts");
Directory.CreateDirectory(args[1]);

foreach (string source in Directory.EnumerateFiles(args[0], "*.md").Order(StringComparer.Ordinal))
{
    string output = Path.Combine(args[1], Path.GetFileNameWithoutExtension(source) + ".pdf");
    byte[] pdf = PdfRenderer.Render(File.ReadAllLines(source), fontDir);
    File.WriteAllBytes(output, pdf);
    Console.WriteLine($"{Path.GetFileName(source)} -> {output} ({pdf.Length / 1024} KB)");
}

return 0;
