using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SelimBank.Api.Customers;
using SelimBank.Api.Data;

namespace SelimBank.Api.Endpoints;

public static class BankEndpoints
{
    public const int DefaultTake = 10;
    public const int MaxTake = 100;
    public const int MaxComplaintLength = 1000;

    public static void MapBankEndpoints(this IEndpointRouteBuilder app)
    {
        // Demo customer picker; no header needed, returns only id and name.
        app.MapGet("/customers", ListCustomers);

        var customer = app.MapGroup("/").RequireCustomer();
        customer.MapGet("/accounts", ListAccounts);
        customer.MapGet("/accounts/{id:int}/transactions", ListTransactions);
        customer.MapGet("/cards", ListCards);
        customer.MapPost("/cards/{id:int}/block", BlockCard);
        customer.MapPost("/complaints", CreateComplaint);
    }

    private static async Task<Ok<List<CustomerSummary>>> ListCustomers(SelimBankDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await db.Customers
            .OrderBy(c => c.Id)
            .Select(c => new CustomerSummary(c.Id, c.FirstName + " " + c.LastName))
            .ToListAsync(ct));

    private static async Task<Ok<List<AccountDto>>> ListAccounts(
        CurrentCustomer customer, SelimBankDbContext db, CancellationToken ct)
    {
        var accounts = await db.Accounts.Where(a => a.CustomerId == customer.Id).OrderBy(a => a.Id).ToListAsync(ct);
        return TypedResults.Ok(accounts.Select(a => a.ToDto()).ToList());
    }

    /// <param name="direction"><c>out</c>: spending only, <c>in</c>: income only, omitted: both.</param>
    private static async Task<Results<Ok<List<TransactionDto>>, ValidationProblem, ProblemHttpResult>> ListTransactions(
        int id, int? take, string? direction, CurrentCustomer customer, SelimBankDbContext db, CancellationToken ct)
    {
        int count = take ?? DefaultTake;
        if (count is < 1 or > MaxTake)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["take"] = [$"take 1 ile {MaxTake} arasında olmalı."],
            });
        }

        if (direction is not (null or "out" or "in"))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["direction"] = ["direction 'out' (harcama) ya da 'in' (gelir) olmalı."],
            });
        }

        var account = await db.Accounts.SingleOrDefaultAsync(a => a.Id == id && a.CustomerId == customer.Id, ct);
        if (account is null)
        {
            return NotFound();
        }

        var query = db.Transactions.Where(t => t.AccountId == account.Id);
        query = direction switch
        {
            "out" => query.Where(t => t.Amount < 0),
            "in" => query.Where(t => t.Amount > 0),
            _ => query,
        };

        var transactions = await query
            .OrderByDescending(t => t.TimestampUtc)
            .ThenByDescending(t => t.Id)
            .Take(count)
            .ToListAsync(ct);

        var cardLast4 = await db.Cards
            .Where(c => c.CustomerId == customer.Id)
            .ToDictionaryAsync(c => c.Id, c => DtoMapping.Last4(c.Number), ct);

        return TypedResults.Ok(transactions
            .Select(t => new TransactionDto(
                t.Id,
                DtoMapping.AsUtc(t.TimestampUtc),
                t.Amount,
                account.Currency,
                t.Description,
                t.Category,
                t.CardId is { } cardId ? cardLast4[cardId] : null))
            .ToList());
    }

    private static async Task<Ok<List<CardDto>>> ListCards(
        CurrentCustomer customer, SelimBankDbContext db, CancellationToken ct)
    {
        var cards = await db.Cards.Where(c => c.CustomerId == customer.Id).OrderBy(c => c.Id).ToListAsync(ct);
        return TypedResults.Ok(cards.Select(c => c.ToDto()).ToList());
    }

    /// <summary>Idempotent: blocking an already blocked card returns it unchanged.</summary>
    private static async Task<Results<Ok<CardDto>, ProblemHttpResult>> BlockCard(
        int id, CurrentCustomer customer, SelimBankDbContext db, TimeProvider time, CancellationToken ct)
    {
        var card = await db.Cards.SingleOrDefaultAsync(c => c.Id == id && c.CustomerId == customer.Id, ct);
        if (card is null)
        {
            return NotFound();
        }

        if (card.Status != CardStatus.Blocked)
        {
            card.Status = CardStatus.Blocked;
            card.BlockedAtUtc = time.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.Ok(card.ToDto());
    }

    private static async Task<Results<Created<ComplaintDto>, ValidationProblem, ProblemHttpResult>> CreateComplaint(
        CreateComplaintRequest request, CurrentCustomer customer, SelimBankDbContext db, TimeProvider time,
        CancellationToken ct)
    {
        string description = request.Description?.Trim() ?? "";
        if (description.Length is 0 or > MaxComplaintLength)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["description"] = [$"Açıklama boş olamaz ve en fazla {MaxComplaintLength} karakter olabilir."],
            });
        }

        if (request.TransactionId is { } transactionId)
        {
            bool owned = await db.Transactions.AnyAsync(
                t => t.Id == transactionId && db.Accounts.Any(a => a.Id == t.AccountId && a.CustomerId == customer.Id),
                ct);
            if (!owned)
            {
                return NotFound();
            }
        }

        var complaint = new Complaint
        {
            CustomerId = customer.Id,
            TransactionId = request.TransactionId,
            Description = description,
            Status = ComplaintStatus.Open,
            CreatedAtUtc = time.GetUtcNow().UtcDateTime,
        };
        db.Complaints.Add(complaint);
        await db.SaveChangesAsync(ct);

        // There is no GET /complaints/{id} endpoint, so no Location header.
        return TypedResults.Created((string?)null, complaint.ToDto());
    }

    // Same answer for "does not exist" and "belongs to someone else": a 403 would reveal that the record exists.
    private static ProblemHttpResult NotFound() =>
        TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Kayıt bulunamadı");
}
