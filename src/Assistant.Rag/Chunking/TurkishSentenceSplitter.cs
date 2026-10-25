namespace Assistant.Rag.Chunking;

/// <summary>
/// Splits Turkish text into sentences. A '.', '!', '?' or '…' ends a sentence only when followed by
/// whitespace; so "1.250,75", "24.10.2026" and "%4,25" stay intact. Common abbreviations and
/// list markers ("1. ") do not end a sentence.
/// </summary>
public static class TurkishSentenceSplitter
{
    private static readonly HashSet<string> Abbreviations = new(StringComparer.Ordinal)
    {
        "vb", "vs", "bkz", "örn", "md", "Md", "mad", "No", "no", "Nu", "s", "sf", "yy", "Dr", "Prof", "Doç",
        "Av", "Sn", "T.C", "A.Ş", "Ltd", "Şti", "Tic", "San", "Cad", "Sok", "Mah", "Apt", "Bşk", "Gn", "Müd",
    };

    public static IReadOnlyList<string> Split(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var sentences = new List<string>();
        int start = 0;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c is not ('.' or '!' or '?' or '…'))
            {
                continue;
            }

            // Consume closing quotes/brackets and repeated punctuation ("?!", "...", '."').
            int end = i;
            while (end + 1 < text.Length && ".!?…\"'”’)".Contains(text[end + 1], StringComparison.Ordinal))
            {
                end++;
            }

            bool atEnd = end + 1 >= text.Length;
            if (!atEnd && !char.IsWhiteSpace(text[end + 1]))
            {
                i = end;
                continue;
            }

            if (c == '.' && !IsSentenceEnd(text, start, i))
            {
                i = end;
                continue;
            }

            Add(sentences, text[start..(end + 1)]);
            start = end + 1;
            i = end;
        }

        if (start < text.Length)
        {
            Add(sentences, text[start..]);
        }

        return sentences;
    }

    private static bool IsSentenceEnd(string text, int sentenceStart, int dot)
    {
        int wordStart = dot;
        while (wordStart > sentenceStart && !char.IsWhiteSpace(text[wordStart - 1]))
        {
            wordStart--;
        }

        string word = text[wordStart..dot].TrimStart('(', '"', '“', '\'');
        if (Abbreviations.Contains(word))
        {
            return false;
        }

        // "1. Kart..." list marker at the start of a sentence.
        bool firstWord = string.IsNullOrWhiteSpace(text[sentenceStart..wordStart]);
        return !(firstWord && word.Length is > 0 and <= 3 && word.All(char.IsAsciiDigit));
    }

    private static void Add(List<string> sentences, string sentence)
    {
        string trimmed = sentence.Trim();
        if (trimmed.Length > 0)
        {
            sentences.Add(trimmed);
        }
    }
}
