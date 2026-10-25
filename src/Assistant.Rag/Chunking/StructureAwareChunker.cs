using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Assistant.Rag.Documents;

namespace Assistant.Rag.Chunking;

public sealed record ChunkingOptions
{
    /// <summary>A unit (article, question, section text) up to this size stays one chunk.</summary>
    public int MaxTokens { get; init; } = 500;

    /// <summary>Window size when a longer unit must be split.</summary>
    public int TargetTokens { get; init; } = 400;

    /// <summary>Sentences carried over between consecutive windows (~10% of the target).</summary>
    public int OverlapTokens { get; init; } = 40;
}

/// <summary>
/// Chunks a <see cref="SourceDocument"/> along its structure: every article ("Madde N"),
/// question ("Soru N") or heading section becomes its own unit, and units are never merged.
/// Units longer than <see cref="ChunkingOptions.MaxTokens"/> are split at sentence boundaries with overlap.
/// </summary>
public sealed class StructureAwareChunker(ChunkingOptions? options = null)
{
    private readonly ChunkingOptions _options = options ?? new ChunkingOptions();

    private sealed record Unit(IReadOnlyList<string> Path, string? Label, List<DocumentBlock> Paragraphs);

    public IReadOnlyList<DocumentChunk> Chunk(SourceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var chunks = new List<DocumentChunk>();
        foreach (var unit in BuildUnits(document))
        {
            var pieces = Split(unit.Paragraphs.Select(p => p.Text).ToList());
            for (int i = 0; i < pieces.Count; i++)
            {
                int index = chunks.Count;
                chunks.Add(new DocumentChunk(
                    CreateId(document.DocId, index, pieces[i]),
                    document.DocId,
                    document.Title,
                    unit.Path,
                    unit.Label,
                    unit.Paragraphs.Min(p => p.PageStart),
                    unit.Paragraphs.Max(p => p.PageEnd),
                    index,
                    i + 1,
                    pieces.Count,
                    pieces[i]));
            }
        }

        return chunks;
    }

    /// <summary>Rough token count for Turkish text (~3.5 characters per token for multilingual tokenizers).</summary>
    public static int EstimateTokens(string text) => (int)Math.Ceiling(text.Length / 3.5);

    private static List<Unit> BuildUnits(SourceDocument document)
    {
        var units = new List<Unit>();
        var headings = new List<DocumentBlock>();
        Unit? current = null;

        foreach (var block in document.Blocks)
        {
            if (block.Kind == BlockKind.Heading)
            {
                current = null;
                headings.RemoveAll(h => h.Level >= block.Level);
                headings.Add(block);
                continue;
            }

            // A paragraph starting with "MADDE N" opens a new article even without a heading in between.
            string? paragraphLabel = ArticleLabels.Label(block.Text);
            if (current is null || (paragraphLabel is not null && current.Paragraphs.Count > 0))
            {
                var path = headings.Select(h => h.Text).ToList();
                if (path.Count > 0 && path[0] == document.Title)
                {
                    path.RemoveAt(0);
                }

                string? headingLabel = headings.Count > 0 ? ArticleLabels.Label(headings[^1].Text) : null;
                current = new Unit(path, paragraphLabel ?? headingLabel, []);
                units.Add(current);
            }

            current.Paragraphs.Add(block);
        }

        return units;
    }

    private List<string> Split(List<string> paragraphs)
    {
        string whole = string.Join('\n', paragraphs);
        if (EstimateTokens(whole) <= _options.MaxTokens)
        {
            return [whole];
        }

        // Sentences keep a trailing '\n' when they end a paragraph, so windows preserve paragraph breaks.
        var sentences = paragraphs
            .SelectMany(p =>
            {
                var s = TurkishSentenceSplitter.Split(p).SelectMany(SplitOversized).ToList();
                s[^1] += "\n";
                return s;
            })
            .ToList();

        var windows = new List<string>();
        var window = new List<string>();
        int tokens = 0;

        foreach (string sentence in sentences)
        {
            int sentenceTokens = EstimateTokens(sentence);
            if (window.Count > 0 && tokens + sentenceTokens > _options.TargetTokens)
            {
                windows.Add(Join(window));
                window = Overlap(window);
                tokens = window.Sum(EstimateTokens);
            }

            window.Add(sentence);
            tokens += sentenceTokens;
        }

        if (window.Count > 0)
        {
            windows.Add(Join(window));
        }

        return windows;
    }

    // Trailing sentences of the previous window, up to the overlap budget.
    private List<string> Overlap(List<string> window)
    {
        var carried = new List<string>();
        int tokens = 0;
        for (int i = window.Count - 1; i > 0; i--)
        {
            int t = EstimateTokens(window[i]);
            if (tokens + t > _options.OverlapTokens)
            {
                break;
            }

            carried.Insert(0, window[i]);
            tokens += t;
        }

        return carried;
    }

    // A single sentence longer than the target is cut at word boundaries.
    private IEnumerable<string> SplitOversized(string sentence)
    {
        if (EstimateTokens(sentence) <= _options.TargetTokens)
        {
            yield return sentence;
            yield break;
        }

        var part = new StringBuilder();
        foreach (string word in sentence.Split(' '))
        {
            if (part.Length > 0 && EstimateTokens(part.ToString()) + EstimateTokens(word) > _options.TargetTokens)
            {
                yield return part.ToString();
                part.Clear();
            }

            part.Append(part.Length > 0 ? " " : "").Append(word);
        }

        if (part.Length > 0)
        {
            yield return part.ToString();
        }
    }

    private static string Join(List<string> sentences)
    {
        var sb = new StringBuilder();
        foreach (string s in sentences)
        {
            if (sb.Length > 0 && sb[^1] != '\n')
            {
                sb.Append(' ');
            }

            sb.Append(s);
        }

        return sb.ToString().TrimEnd();
    }

    private static Guid CreateId(string docId, int index, string text)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{docId}\n{index.ToString(CultureInfo.InvariantCulture)}\n{text}"));
        return new Guid(hash.AsSpan(0, 16));
    }
}
