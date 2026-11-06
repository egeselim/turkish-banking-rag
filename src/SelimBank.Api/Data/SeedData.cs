using Bogus;

namespace SelimBank.Api.Data;

public sealed record SeedSet(
    IReadOnlyList<Customer> Customers,
    IReadOnlyList<Account> Accounts,
    IReadOnlyList<Card> Cards,
    IReadOnlyList<Transaction> Transactions);

/// <summary>
/// Deterministic demo data: the same seed always yields the same customers, accounts, cards and transactions.
/// Only Bogus (tr) and algorithms are used; no real person data.
/// </summary>
public static class SeedData
{
    public const int DefaultSeed = 20261105;
    public const int CustomerCount = 5;

    /// <summary>
    /// Fixed "now" for generated dates. Bogus' date helpers read the wall clock, which would make two startups differ.
    /// </summary>
    public static readonly DateTime AnchorUtc = new(2026, 11, 1, 18, 0, 0, DateTimeKind.Utc);

    // Fictional bank code; the bank itself is fictional too.
    private const string BankCode = "00999";
    private const int HistoryDays = 90;

    private static readonly SpendingKind[] Spending =
    [
        new("Papatya Market", "Market", 45m, 1200m, ByCard: true),
        new("Köşebaşı Fırın", "Market", 15m, 180m, ByCard: true),
        new("Lale Kafe", "Restoran", 60m, 650m, ByCard: true),
        new("Martı Restoran", "Restoran", 250m, 2400m, ByCard: true),
        new("Toplu Ulaşım Dolum", "Ulaşım", 20m, 400m, ByCard: true),
        new("Yakamoz Akaryakıt", "Akaryakıt", 500m, 3000m, ByCard: true),
        new("Sayfa Kitabevi", "Alışveriş", 90m, 900m, ByCard: true),
        new("Çınar Giyim", "Alışveriş", 300m, 4000m, ByCard: true),
        new("Nergis Eczanesi", "Sağlık", 40m, 800m, ByCard: true),
        new("Elektrik Faturası", "Fatura", 300m, 1500m, ByCard: false),
        new("İnternet Faturası", "Fatura", 250m, 600m, ByCard: false),
    ];

    public static SeedSet Generate(int seed = DefaultSeed)
    {
        // A local Randomizer: the global Randomizer.Seed is shared state and tests run in parallel.
        var random = new Randomizer(seed);
        var faker = new Faker("tr") { Random = random };

        var customers = new List<Customer>();
        var accounts = new List<Account>();
        var cards = new List<Card>();
        var transactions = new List<Transaction>();

        for (int customerId = 1; customerId <= CustomerCount; customerId++)
        {
            string firstName = faker.Name.FirstName();
            string lastName = faker.Name.LastName();
            customers.Add(new Customer
            {
                Id = customerId,
                FirstName = firstName,
                LastName = lastName,
                Tckn = Identifiers.Tckn(random),
                Phone = "5" + random.ReplaceNumbers("#########"),
                Email = faker.Internet.Email(firstName, lastName, "ornek.com.tr"),
            });

            var current = NewAccount(accounts, customerId, "Vadesiz TL Hesabı", random);
            current.Balance = RoundMoney(random.Decimal(2_000m, 20_000m));

            var customerCards = new List<Card> { NewCard(cards, customerId, current.Id, CardKind.Debit, random) };
            if (random.Bool())
            {
                customerCards.Add(NewCard(cards, customerId, current.Id, CardKind.Credit, random));
            }

            var history = new List<Transaction>();
            for (int month = 0; month < 3; month++)
            {
                history.Add(NewTransaction(current, null, AnchorUtc.AddDays(-30 * month - 1).Date.AddHours(9),
                    RoundMoney(random.Decimal(25_000m, 60_000m)), "Maaş Ödemesi", "Maaş"));
            }

            int spendingCount = random.Number(25, 40);
            for (int i = 0; i < spendingCount; i++)
            {
                var kind = random.ArrayElement(Spending);
                var card = kind.ByCard ? random.ListItem(customerCards) : null;
                history.Add(NewTransaction(current, card, RandomPastTime(random),
                    -RoundMoney(random.Decimal(kind.Min, kind.Max)), kind.Description, kind.Category));
            }

            if (random.Bool())
            {
                var savings = NewAccount(accounts, customerId, "Birikim Hesabı", random);
                savings.Balance = RoundMoney(random.Decimal(10_000m, 150_000m));
                int transferCount = random.Number(2, 5);
                for (int i = 0; i < transferCount; i++)
                {
                    history.Add(NewTransaction(savings, null, RandomPastTime(random),
                        RoundMoney(random.Decimal(1_000m, 10_000m)), "Gelen Havale", "Havale"));
                }
            }

            transactions.AddRange(history);
        }

        // Ids follow time order; OrderBy is stable, so equal timestamps keep generation order.
        var ordered = transactions.OrderBy(t => t.TimestampUtc).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            ordered[i].Id = i + 1;
        }

        return new SeedSet(customers, accounts, cards, ordered);
    }

    private static Account NewAccount(List<Account> accounts, int customerId, string name, Randomizer random)
    {
        var account = new Account
        {
            Id = accounts.Count + 1,
            CustomerId = customerId,
            Name = name,
            Iban = Identifiers.TrIban(random, BankCode),
            Currency = "TRY",
        };
        accounts.Add(account);
        return account;
    }

    private static Card NewCard(List<Card> cards, int customerId, int accountId, CardKind kind, Randomizer random)
    {
        var card = new Card
        {
            Id = cards.Count + 1,
            CustomerId = customerId,
            AccountId = accountId,
            Number = Identifiers.CardNumber(random, kind == CardKind.Debit ? "4" : "5"),
            Kind = kind,
            Status = CardStatus.Active,
        };
        cards.Add(card);
        return card;
    }

    /// <summary>Also keeps the balance consistent: opening balance + every transaction.</summary>
    private static Transaction NewTransaction(
        Account account, Card? card, DateTime timestampUtc, decimal amount, string description, string category)
    {
        account.Balance += amount;
        return new Transaction
        {
            AccountId = account.Id,
            CardId = card?.Id,
            TimestampUtc = timestampUtc,
            Amount = amount,
            Description = description,
            Category = category,
        };
    }

    private static DateTime RandomPastTime(Randomizer random) =>
        AnchorUtc.AddMinutes(-random.Number(60, HistoryDays * 24 * 60));

    private static decimal RoundMoney(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private sealed record SpendingKind(string Description, string Category, decimal Min, decimal Max, bool ByCard);
}
