namespace SelimBank.Api.Data;

// Persistence model. Identifiers such as TCKN, full IBAN and full card number live only here;
// endpoints map entities to DTOs (Endpoints/Dtos.cs) that never carry them.

public sealed class Customer
{
    public int Id { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public required string Tckn { get; set; }
    public required string Phone { get; set; }
    public required string Email { get; set; }
}

public sealed class Account
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public required string Name { get; set; }
    public required string Iban { get; set; }
    public required string Currency { get; set; }
    public decimal Balance { get; set; }
}

public enum CardKind
{
    Debit,
    Credit,
}

public enum CardStatus
{
    Active,
    Blocked,
}

public sealed class Card
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public int AccountId { get; set; }
    public required string Number { get; set; }
    public CardKind Kind { get; set; }
    public CardStatus Status { get; set; }
    public DateTime? BlockedAtUtc { get; set; }
}

public sealed class Transaction
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public int? CardId { get; set; }
    public DateTime TimestampUtc { get; set; }

    /// <summary>Negative for spending, positive for income.</summary>
    public decimal Amount { get; set; }

    public required string Description { get; set; }
    public required string Category { get; set; }
}

public enum ComplaintStatus
{
    Open,
}

public sealed class Complaint
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public int? TransactionId { get; set; }
    public required string Description { get; set; }
    public ComplaintStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
