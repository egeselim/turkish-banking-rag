using System.ComponentModel;
using System.Globalization;
using Microsoft.Extensions.AI;

namespace Assistant.Core.Banking;

/// <summary>
/// Agent tools bound to one customer. They are created per turn from the server-side session, so the customer id
/// is never a tool parameter: the model can neither see nor choose it. Ownership is enforced again by the API.
/// Results carry ids, last-4 digits and pre-formatted Turkish amounts and dates (the model should not reformat
/// numbers itself).
/// </summary>
internal sealed class BankingTools(SelimBankClient bank, int customerId)
{
    public const int MaxTransactions = 20;

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly TimeZoneInfo Istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    public IList<AITool> Create() =>
    [
        AIFunctionFactory.Create(GetBalanceAsync, "GetBalance",
            "Müşterinin hesaplarını ve güncel bakiyelerini getirir."),
        AIFunctionFactory.Create(ListTransactionsAsync, "ListTransactions",
            "Müşterinin son hesap hareketlerini yeniden eskiye doğru getirir. Sonuçtaki 'number' alanı listedeki sıradır."),
    ];

    private async Task<IReadOnlyList<AccountView>> GetBalanceAsync(CancellationToken cancellationToken)
    {
        var accounts = await bank.GetAccountsAsync(customerId, cancellationToken).ConfigureAwait(false);
        return [.. accounts.Select(a => new AccountView(a.Id, a.Name, a.IbanLast4, Money(a.Balance, a.Currency)))];
    }

    private async Task<TransactionList> ListTransactionsAsync(
        [Description("Kaç işlem getirileceği (1-20).")] int count = 5,
        [Description("'spending': yalnızca harcamalar, 'income': yalnızca gelen paralar, 'all': hepsi.")] string kind = "all",
        [Description("Yalnızca bu hesabın işlemleri (GetBalance'taki accountId). Boş bırakılırsa tüm hesaplar.")] int? accountId = null,
        CancellationToken cancellationToken = default)
    {
        count = Math.Clamp(count, 1, MaxTransactions);
        var direction = kind switch
        {
            "spending" => TransactionDirection.Spending,
            "income" => TransactionDirection.Income,
            _ => TransactionDirection.All,
        };

        IEnumerable<int> accountIds = accountId is { } id
            ? [id]
            : (await bank.GetAccountsAsync(customerId, cancellationToken).ConfigureAwait(false)).Select(a => a.Id);

        var all = new List<BankTransaction>();
        foreach (int account in accountIds)
        {
            var transactions = await bank.GetTransactionsAsync(customerId, account, count, direction, cancellationToken)
                .ConfigureAwait(false);
            if (transactions is null)
            {
                return new TransactionList("Bu hesap bulunamadı.", []);
            }

            all.AddRange(transactions);
        }

        var newest = all.OrderByDescending(t => t.Date).ThenByDescending(t => t.Id).Take(count);
        return new TransactionList(null, [.. newest.Select((t, i) => new TransactionView(
            i + 1, t.Id, LocalTime(t.Date), Money(t.Amount, t.Currency), t.Description, t.Category, t.CardLast4))]);
    }

    internal static string Money(decimal amount, string currency) =>
        amount.ToString("#,##0.00", Turkish) + (currency == "TRY" ? " TL" : " " + currency);

    internal static string LocalTime(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Istanbul).ToString("dd.MM.yyyy HH:mm", Turkish);

    internal sealed record AccountView(int AccountId, string Name, string IbanLast4, string Balance);

    internal sealed record TransactionView(
        int Number, int TransactionId, string Date, string Amount, string Description, string Category, string? CardLast4);

    internal sealed record TransactionList(string? Message, IReadOnlyList<TransactionView> Transactions);
}
