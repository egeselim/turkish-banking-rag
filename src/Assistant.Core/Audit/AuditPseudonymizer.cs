using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Assistant.Core.Audit;

/// <summary>
/// Turns a customer id into a pseudonym with HMAC-SHA256. A plain hash would not do: ids are small numbers, so
/// anyone could hash 1, 2, 3... and compare. Without the key that comparison is impossible.
/// With no configured key a random one is used: everything works, but pseudonyms change on every restart.
/// </summary>
public sealed class AuditPseudonymizer
{
    public const string NoCustomer = "musteri-yok";

    private const int PseudonymBytes = 8;

    private readonly byte[] _key;

    public AuditPseudonymizer(string? key, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            _key = RandomNumberGenerator.GetBytes(32);
            if (logger is not null)
            {
                AuditLogMessages.PseudonymKeyMissing(logger);
            }
        }
        else
        {
            _key = Encoding.UTF8.GetBytes(key);
        }
    }

    public string Pseudonymize(int? customerId)
    {
        if (customerId is null)
        {
            return NoCustomer;
        }

        byte[] hash = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(customerId.Value.ToString(CultureInfo.InvariantCulture)));
        return Convert.ToHexStringLower(hash.AsSpan(0, PseudonymBytes));
    }
}
