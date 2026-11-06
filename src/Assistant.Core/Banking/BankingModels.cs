namespace Assistant.Core.Banking;

// Core's own copy of the SelimBank.Api response contracts: Core talks to the API over HTTP only
// and does not reference that project (see ArchitectureTests).

public sealed record BankCustomer(int Id, string Name);

public sealed record BankAccount(int Id, string Name, string IbanLast4, string Currency, decimal Balance);

/// <param name="Amount">Negative for spending, positive for income.</param>
public sealed record BankTransaction(
    int Id, DateTime Date, decimal Amount, string Currency, string Description, string Category, string? CardLast4);

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

public sealed record BankCard(int Id, CardKind Kind, string Last4, CardStatus Status, DateTime? BlockedAt);

public sealed record BankComplaint(int Id, int? TransactionId, string Status, DateTime CreatedAt);

public enum TransactionDirection
{
    All,
    Spending,
    Income,
}
