using System.Collections;
using System.Globalization;
using Assistant.Privacy;

namespace SelimBank.ServiceDefaults.Telemetry;

internal static class TelemetryScrubbing
{
    /// <summary>
    /// Strings are masked; arrays of strings element by element; other values only if their text form holds PII
    /// (then the masked text replaces them). Numbers and booleans normally pass through unchanged.
    /// </summary>
    public static (object? Value, bool Changed) MaskValue(IPiiMasker masker, object? value)
    {
        switch (value)
        {
            case null:
                return (null, false);
            case string text:
            {
                string masked = masker.Mask(text);
                return (masked, !ReferenceEquals(masked, text) && masked != text);
            }

            case IEnumerable items and not IDictionary:
            {
                var texts = items.Cast<object?>().Select(i => Convert.ToString(i, CultureInfo.InvariantCulture) ?? "").ToArray();
                var masked = texts.Select(masker.Mask).ToArray();
                return texts.SequenceEqual(masked) ? (value, false) : (masked, true);
            }

            default:
            {
                string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
                string masked = masker.Mask(text);
                return masked == text ? (value, false) : (masked, true);
            }
        }
    }
}
