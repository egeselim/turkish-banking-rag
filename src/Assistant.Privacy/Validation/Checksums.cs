namespace Assistant.Privacy.Validation;

/// <summary>
/// Checksum algorithms that separate real identifiers from look-alike numbers
/// (order numbers, amounts, dates), so only valid ones are masked.
/// </summary>
public static class Checksums
{
    /// <summary>
    /// T.C. Kimlik No: 11 digits, first digit not 0,
    /// d10 = ((d1+d3+d5+d7+d9)*7 − (d2+d4+d6+d8)) mod 10, d11 = (d1+…+d10) mod 10.
    /// </summary>
    public static bool IsValidTckn(ReadOnlySpan<char> value)
    {
        if (value.Length != 11 || value[0] == '0' || !AllDigits(value))
        {
            return false;
        }

        int odd = 0, even = 0, firstTen = 0;
        for (int i = 0; i < 9; i++)
        {
            int d = value[i] - '0';
            if (i % 2 == 0)
            {
                odd += d;
            }
            else
            {
                even += d;
            }
        }

        for (int i = 0; i < 10; i++)
        {
            firstTen += value[i] - '0';
        }

        int d10 = (((odd * 7) - even) % 10 + 10) % 10;
        return d10 == value[9] - '0' && firstTen % 10 == value[10] - '0';
    }

    /// <summary>Luhn (mod 10) check over a digits-only string.</summary>
    public static bool IsValidLuhn(ReadOnlySpan<char> digits)
    {
        if (digits.Length == 0 || !AllDigits(digits))
        {
            return false;
        }

        int sum = 0;
        bool doubleIt = false;
        for (int i = digits.Length - 1; i >= 0; i--)
        {
            int d = digits[i] - '0';
            if (doubleIt)
            {
                d *= 2;
                if (d > 9)
                {
                    d -= 9;
                }
            }

            sum += d;
            doubleIt = !doubleIt;
        }

        return sum % 10 == 0;
    }

    /// <summary>ISO 13616 IBAN check (mod 97 == 1) over a compact, upper-case IBAN.</summary>
    public static bool IsValidIban(ReadOnlySpan<char> iban)
    {
        if (iban.Length < 15 || iban.Length > 34)
        {
            return false;
        }

        // Move the country code and check digits to the end, letters become 10..35.
        int remainder = 0;
        for (int i = 0; i < iban.Length; i++)
        {
            char c = iban[(i + 4) % iban.Length];
            if (char.IsAsciiDigit(c))
            {
                remainder = ((remainder * 10) + (c - '0')) % 97;
            }
            else if (char.IsAsciiLetterUpper(c))
            {
                remainder = ((remainder * 100) + (c - 'A' + 10)) % 97;
            }
            else
            {
                return false;
            }
        }

        return remainder == 1;
    }

    private static bool AllDigits(ReadOnlySpan<char> value)
    {
        foreach (char c in value)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}
