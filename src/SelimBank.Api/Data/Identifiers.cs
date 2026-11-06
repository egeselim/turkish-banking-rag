using System.Globalization;
using System.Text;
using Bogus;

namespace SelimBank.Api.Data;

/// <summary>
/// Synthetic, checksum-valid identifiers. A deliberate small copy of the test generator in
/// tests/Assistant.Privacy.Tests/TestData/FakePii.cs: SelimBank.Api must not reference Assistant.Privacy.
/// SelimBank.Api.Tests verifies every generated value with Privacy's <c>Checksums</c>.
/// </summary>
internal static class Identifiers
{
    /// <summary>T.C. Kimlik No: 11 digits, first digit not 0, two check digits.</summary>
    public static string Tckn(Randomizer r)
    {
        var d = new int[11];
        d[0] = r.Number(1, 9);
        for (int i = 1; i < 9; i++)
        {
            d[i] = r.Number(0, 9);
        }

        int odd = d[0] + d[2] + d[4] + d[6] + d[8];
        int even = d[1] + d[3] + d[5] + d[7];
        d[9] = (((odd * 7) - even) % 10 + 10) % 10;
        d[10] = d.Take(10).Sum() % 10;
        return string.Concat(d);
    }

    /// <summary>Card number with a Luhn check digit.</summary>
    public static string CardNumber(Randomizer r, string prefix, int length = 16)
    {
        var sb = new StringBuilder(prefix);
        while (sb.Length < length - 1)
        {
            sb.Append(r.Number(0, 9));
        }

        return sb.Append(LuhnCheckDigit(sb.ToString())).ToString();
    }

    /// <summary>TR IBAN: TR + 2 check digits + 5-digit bank code + reserved '0' + 16-digit account number.</summary>
    public static string TrIban(Randomizer r, string bankCode)
    {
        string bban = bankCode + "0" + r.ReplaceNumbers("################");
        int mod = Mod97(bban + "2927" + "00"); // "TR00" moved to the end, T=29, R=27.
        return string.Create(CultureInfo.InvariantCulture, $"TR{98 - mod:00}{bban}");
    }

    private static int LuhnCheckDigit(string payload)
    {
        int sum = 0;
        bool doubleIt = true; // the check digit will be appended to the right
        for (int i = payload.Length - 1; i >= 0; i--)
        {
            int d = payload[i] - '0';
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

        return (10 - (sum % 10)) % 10;
    }

    private static int Mod97(string digits)
    {
        int remainder = 0;
        foreach (char c in digits)
        {
            remainder = ((remainder * 10) + (c - '0')) % 97;
        }

        return remainder;
    }
}
