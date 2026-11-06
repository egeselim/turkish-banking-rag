using SelimBank.Api.Data;

namespace SelimBank.Api.Endpoints;

// Response contracts. They carry ids and last-4 digits only: never a TCKN, full IBAN or full card number.

public sealed record CustomerSummary(int Id, string Name);

public sealed record AccountDto(int Id, string Name, string IbanLast4, string Currency, decimal Balance);

public sealed record TransactionDto(
    int Id, DateTime Date, decimal Amount, string Currency, string Description, string Category, string? CardLast4);

public sealed record CardDto(int Id, CardKind Kind, string Last4, CardStatus Status, DateTime? BlockedAt);

public sealed record CreateComplaintRequest(string? Description, int? TransactionId);

public sealed record ComplaintDto(int Id, int? TransactionId, ComplaintStatus Status, DateTime CreatedAt);

internal static class DtoMapping
{
    public static AccountDto ToDto(this Account a) => new(a.Id, a.Name, Last4(a.Iban), a.Currency, a.Balance);

    public static CardDto ToDto(this Card c) => new(c.Id, c.Kind, Last4(c.Number), c.Status, AsUtc(c.BlockedAtUtc));

    public static ComplaintDto ToDto(this Complaint c) => new(c.Id, c.TransactionId, c.Status, AsUtc(c.CreatedAtUtc));

    public static string Last4(string value) => value[^4..];

    // SQLite returns DateTime with Kind=Unspecified; the stored values are UTC.
    public static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    public static DateTime? AsUtc(DateTime? value) => value is { } v ? AsUtc(v) : null;
}
