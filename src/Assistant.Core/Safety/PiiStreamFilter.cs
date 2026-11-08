using System.Text;
using Assistant.Privacy;

namespace Assistant.Core.Safety;

/// <summary>
/// Masks PII in text that arrives in pieces. A card number can be split across two pieces, so the end of the text
/// is held back until more arrives. The cut is made at whitespace, so a space-free value (an e-mail) is never split,
/// and the held-back window is wider than the longest spaced value (an IBAN written in groups, about 32 characters).
/// </summary>
internal sealed class PiiStreamFilter(IPiiMasker masker)
{
    internal const int HoldBack = 64;

    private readonly StringBuilder _buffer = new();

    /// <summary>Adds a piece and returns the part that is now safe to show (may be empty).</summary>
    public string Push(string text)
    {
        _buffer.Append(text);
        if (_buffer.Length <= HoldBack)
        {
            return "";
        }

        string masked = masker.Mask(_buffer.ToString());
        int cut = masked.LastIndexOf(' ', Math.Max(masked.Length - HoldBack - 1, 0));
        if (cut < 0)
        {
            return "";
        }

        _buffer.Clear().Append(masked, cut + 1, masked.Length - cut - 1);
        return masked[..(cut + 1)];
    }

    /// <summary>The text is finished: returns everything still held back.</summary>
    public string Flush()
    {
        string rest = masker.Mask(_buffer.ToString());
        _buffer.Clear();
        return rest;
    }
}
