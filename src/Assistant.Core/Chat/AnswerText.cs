using System.Text.RegularExpressions;

namespace Assistant.Core.Chat;

/// <summary>
/// The chat shows answers as plain text. The prompt asks for no markdown, but the model still emits
/// bold markers and headings now and then; strip those so users never see raw "**".
/// </summary>
public static partial class AnswerText
{
    public static string ToPlain(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return HeadingRegex().Replace(text.Replace("**", "", StringComparison.Ordinal).Replace("__", "", StringComparison.Ordinal), "");
    }

    [GeneratedRegex(@"^#{1,6}\s+", RegexOptions.Multiline)]
    private static partial Regex HeadingRegex();
}
