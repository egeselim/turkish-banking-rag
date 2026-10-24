namespace Assistant.Privacy;

public enum PiiType
{
    /// <summary>T.C. Kimlik Numarası (11 digits, checksum validated).</summary>
    NationalId,

    /// <summary>Payment card number (13–19 digits, Luhn validated).</summary>
    CardNumber,

    /// <summary>Turkish IBAN (TR + 24 digits, mod-97 validated).</summary>
    Iban,

    /// <summary>Turkish mobile number (5xx xxx xx xx, optional +90 / 0 prefix).</summary>
    Phone,

    Email,
}

/// <summary>A detected PII span in the scanned text.</summary>
public readonly record struct PiiMatch(PiiType Type, int Start, int Length)
{
    public int End => Start + Length;

    public bool Overlaps(PiiMatch other) => Start < other.End && other.Start < End;
}
