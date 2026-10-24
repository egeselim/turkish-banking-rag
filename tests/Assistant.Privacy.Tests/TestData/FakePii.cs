using System.Globalization;
using System.Text;
using Bogus;

namespace Assistant.Privacy.Tests.TestData;

/// <summary>
/// Generates synthetic but checksum-valid identifiers. Project rule: test data comes only from Bogus
/// and algorithms, never from real people. Use a seeded <see cref="Randomizer"/> for reproducibility.
/// </summary>
public static class FakePii
{
    public static Randomizer Seeded(int seed = 20261024) => new(seed);

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

    public static string CardNumber(Randomizer r, string prefix = "4", int length = 16)
    {
        var sb = new StringBuilder(prefix);
        while (sb.Length < length - 1)
        {
            sb.Append(r.Number(0, 9));
        }

        return sb.Append(LuhnCheckDigit(sb.ToString())).ToString();
    }

    public static string TrIban(Randomizer r)
    {
        // TR + 2 check digits + 5-digit bank code + reserved '0' + 16-digit account number.
        string bban = r.ReplaceNumbers("#####") + "0" + r.ReplaceNumbers("################");
        int mod = Mod97(bban + "2927" + "00"); // "TR00" moved to the end, T=29, R=27.
        return string.Create(CultureInfo.InvariantCulture, $"TR{98 - mod:00}{bban}");
    }

    /// <summary>10-digit Turkish mobile number without prefix, e.g. 5321234567.</summary>
    public static string Mobile(Randomizer r) => "5" + r.ReplaceNumbers("#########");

    public static string Email(Faker f) => f.Internet.Email(f.Name.FirstName(), f.Name.LastName(), "ornek.com.tr");

    /// <summary>Changes the last digit, which breaks every checksum used here.</summary>
    public static string WithWrongLastDigit(string value)
    {
        char last = value[^1];
        char replacement = last == '9' ? '0' : (char)(last + 1);
        return value[..^1] + replacement;
    }

    public static string Group(string value, int size, char separator) =>
        string.Join(separator, value.Chunk(size).Select(c => new string(c)));

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
