namespace Assistant.Core.Tests.Safety;

/// <summary>Valid-by-algorithm canaries (fixed digits, no real person): TCKN checksum and Luhn card number.</summary>
internal static class SyntheticPii
{
    public static string NationalId()
    {
        int[] d = [1, 2, 3, 4, 5, 6, 7, 8, 9];
        int odd = d[0] + d[2] + d[4] + d[6] + d[8];
        int even = d[1] + d[3] + d[5] + d[7];
        int tenth = (((odd * 7) - even) % 10 + 10) % 10;
        int eleventh = (d.Sum() + tenth) % 10;
        return string.Concat(d) + tenth + eleventh;
    }

    public static string CardNumber()
    {
        const string prefix = "400000123456789";
        int sum = 0;
        for (int i = 0; i < prefix.Length; i++)
        {
            int digit = prefix[prefix.Length - 1 - i] - '0';
            sum += i % 2 == 0 ? (digit * 2 > 9 ? (digit * 2) - 9 : digit * 2) : digit;
        }

        return prefix + ((10 - (sum % 10)) % 10);
    }
}
