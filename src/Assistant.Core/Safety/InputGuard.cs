using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Assistant.Core.Safety;

public enum InputVerdict
{
    Allowed,
    TooLong,
    Injection,
}

/// <summary>
/// Stops oversized messages and obvious prompt-injection phrases before they reach the embedding or the model.
/// A short list on purpose: this is a cheap first filter, not the real protection. The structural controls are
/// (session-bound customer, approvals, ownership checks in the API) hold even when a phrase gets through.
/// Step 2.6 measures how much it catches. Patterns run on folded text ("ÖNCEKİ" and "onceki" look the same).
/// </summary>
public static class InputGuard
{
    public const int MaxLength = 2000;

    private const RegexOptions Options =
        RegexOptions.NonBacktracking | RegexOptions.CultureInvariant | RegexOptions.Multiline;

    private static readonly Regex[] InjectionPatterns =
    [
        // "önceki talimatları yok say", "tüm kuralları unut". Not "talimatımı iptal et" (a real banking request)
        // and not "unuttum" (the customer forgot something).
        new(@"\b(?:onceki|yukaridaki|butun|tum|sistem)\s+(?:\w+\s+){0,2}(?:talimat|kural|yonerge|komut|prompt)\w*\s+(?:\w+\s+){0,2}(?:yok\s*say\w*|unut(?:un)?\b|gormezden\s+gel\w*|uyma\b|uymayin\b)", Options),
        // "sistem promptunu göster". Imperatives only: "sistem mesajı yazıyor" is a customer describing an error.
        new(@"\bsistem\s+(?:prompt|istem|mesaj|talimat|yonerge)\w*\s+(?:\w+\s+){0,2}(?:goster|yaz|soyle|paylas|acikla|ver|tekrarla)(?:in|iniz)?\b", Options),
        new(@"\b(?:ignore|disregard|forget)\s+(?:\w+\s+){0,3}(?:instructions?|rules?|prompts?)\b", Options),
        new(@"\b(?:reveal|show|print|repeat)\s+(?:\w+\s+){0,3}system\s+(?:prompt|message|instructions?)\b", Options),
        new(@"\b(?:developer\s+mode|gelistirici\s+mod\w*|jailbreak)\b", Options),
        // Fake role markers.
        new(@"<\|?\s*(?:system|assistant|im_start|im_end)\s*\|?>|\[/?inst\]|^\s*(?:#+\s*)?(?:system|sistem)\s*:", Options),
    ];

    public static InputVerdict Check(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.Length > MaxLength)
        {
            return InputVerdict.TooLong;
        }

        string folded = Fold(message);
        return InjectionPatterns.Any(p => p.IsMatch(folded)) ? InputVerdict.Injection : InputVerdict.Allowed;
    }

    /// <summary>Lower case without Turkish letters and without invisible characters (a zero-width space can split a phrase).</summary>
    private static string Fold(string message)
    {
        var sb = new StringBuilder(message.Length);
        foreach (char c in message.ToLower(CultureInfo.GetCultureInfo("tr-TR")))
        {
            if (char.GetUnicodeCategory(c) == UnicodeCategory.Format)
            {
                continue;
            }

            sb.Append(c switch
            {
                'ı' => 'i',
                'ş' => 's',
                'ç' => 'c',
                'ğ' => 'g',
                'ü' => 'u',
                'ö' => 'o',
                _ => c,
            });
        }

        return sb.ToString();
    }
}
