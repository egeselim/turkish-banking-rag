using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
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

    public const string BlockCardName = "BlockCard";
    public const string CreateComplaintName = "CreateComplaint";

    /// <summary>
    /// Irreversible tools are wrapped in <see cref="ApprovalRequiredAIFunction"/>: the function-invoking layer does not
    /// run them but surfaces a <see cref="ToolApprovalRequestContent"/>, and Agent Framework's approval-response
    /// binding only honours approvals it issued itself (once, with the surfaced arguments). The rule is in code,
    /// not in the prompt.
    /// </summary>
    public IList<AITool> Create() =>
    [
        AIFunctionFactory.Create(GetBalanceAsync, "GetBalance",
            "Müşterinin hesaplarını ve güncel bakiyelerini getirir."),
        AIFunctionFactory.Create(ListTransactionsAsync, "ListTransactions",
            "Müşterinin son hesap hareketlerini yeniden eskiye doğru getirir. Sonuçtaki 'number' alanı listedeki sıradır."),
        AIFunctionFactory.Create(ListCardsAsync, "ListCards",
            "Müşterinin kartlarını getirir: cardId, tür, son 4 hane ve durum."),
        new ApprovalRequiredAIFunction(AIFunctionFactory.Create(BlockCardAsync, BlockCardName,
            "Müşterinin bir kartını kalıcı olarak bloke eder (geri alınamaz). cardId'yi ListCards'tan al.")),
        new ApprovalRequiredAIFunction(AIFunctionFactory.Create(CreateComplaintAsync, CreateComplaintName,
            "Müşteri adına şikâyet veya işlem itirazı kaydı açar. İtiraz bir işlemle ilgiliyse transactionId'yi ListTransactions'tan al.")),
    ];

    /// <summary>
    /// The approval card text, built from the call's actual arguments and the bank's data (never from model prose),
    /// so the customer approves exactly what will run.
    /// </summary>
    public async Task<string> DescribeAsync(FunctionCallContent call, CancellationToken cancellationToken)
    {
        switch (call.Name)
        {
            case BlockCardName:
            {
                int? cardId = IntArgument(call, "cardId");
                var card = (await bank.GetCardsAsync(customerId, cancellationToken).ConfigureAwait(false))
                    .FirstOrDefault(c => c.Id == cardId);
                return card is null
                    ? "Bilinmeyen bir kart bloke edilmek isteniyor; işlem büyük olasılıkla başarısız olacak."
                    : $"•••• {card.Last4} ile biten {KindText(card.Kind)} kalıcı olarak bloke edilecek. Bu işlem geri alınamaz.";
            }

            case CreateComplaintName:
            {
                string description = StringArgument(call, "description") ?? "";
                int? transactionId = IntArgument(call, "transactionId");
                if (transactionId is null)
                {
                    return $"Şikâyet kaydı açılacak: «{description}»";
                }

                var transaction = await FindTransactionAsync(transactionId.Value, cancellationToken).ConfigureAwait(false);
                return transaction is null
                    ? $"Bilinmeyen bir işlem (#{transactionId}) için şikâyet açılmak isteniyor: «{description}»"
                    : $"{LocalTime(transaction.Date)} tarihli {Money(transaction.Amount, transaction.Currency)} {transaction.Description} işlemi için itiraz kaydı açılacak: «{description}»";
            }

            default:
                return $"{call.Name} işlemi çalıştırılacak.";
        }
    }

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

    private async Task<IReadOnlyList<CardView>> ListCardsAsync(CancellationToken cancellationToken)
    {
        var cards = await bank.GetCardsAsync(customerId, cancellationToken).ConfigureAwait(false);
        return [.. cards.Select(ToView)];
    }

    private async Task<CardResult> BlockCardAsync(
        [Description("Bloke edilecek kartın cardId'si (ListCards).")] int cardId, CancellationToken cancellationToken = default)
    {
        var card = await bank.BlockCardAsync(customerId, cardId, cancellationToken).ConfigureAwait(false);
        return card is null ? new CardResult("Kart bulunamadı.", null) : new CardResult(null, ToView(card));
    }

    private async Task<ComplaintResult> CreateComplaintAsync(
        [Description("Şikâyetin müşterinin ifadesine dayanan kısa açıklaması (en fazla 1000 karakter).")] string description,
        [Description("İtiraz edilen işlemin transactionId'si (ListTransactions). İşlemle ilgili değilse boş bırak.")] int? transactionId = null,
        CancellationToken cancellationToken = default)
    {
        var complaint = await bank.CreateComplaintAsync(customerId, description, transactionId, cancellationToken).ConfigureAwait(false);
        return complaint is null
            ? new ComplaintResult("İşlem bulunamadı; şikâyet açılmadı.", null, null)
            : new ComplaintResult(null, complaint.Id, complaint.Status == "Open" ? "Açık" : complaint.Status);
    }

    /// <summary>Scans the customer's recent transactions; the API has no single-transaction endpoint.</summary>
    private async Task<BankTransaction?> FindTransactionAsync(int transactionId, CancellationToken cancellationToken)
    {
        foreach (var account in await bank.GetAccountsAsync(customerId, cancellationToken).ConfigureAwait(false))
        {
            var transactions = await bank.GetTransactionsAsync(customerId, account.Id, 100, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var match = transactions?.FirstOrDefault(t => t.Id == transactionId);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static CardView ToView(BankCard card) =>
        new(card.Id, KindText(card.Kind), card.Last4, card.Status == CardStatus.Blocked ? "Bloke" : "Aktif");

    private static string KindText(CardKind kind) => kind == CardKind.Credit ? "kredi kartı" : "banka kartı";

    // Arguments arrive as JsonElement from the provider and as CLR values from tests or local callers.
    private static int? IntArgument(FunctionCallContent call, string name) =>
        call.Arguments?.TryGetValue(name, out object? value) != true ? null : value switch
        {
            int i => i,
            long l => (int)l,
            JsonElement { ValueKind: JsonValueKind.Number } e when e.TryGetInt32(out int i) => i,
            JsonElement { ValueKind: JsonValueKind.String } e when int.TryParse(e.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out int i) => i,
            string s when int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out int i) => i,
            _ => null,
        };

    private static string? StringArgument(FunctionCallContent call, string name) =>
        call.Arguments?.TryGetValue(name, out object? value) != true ? null : value switch
        {
            string s => s,
            JsonElement { ValueKind: JsonValueKind.String } e => e.GetString(),
            _ => value?.ToString(),
        };

    internal static string Money(decimal amount, string currency) =>
        amount.ToString("#,##0.00", Turkish) + (currency == "TRY" ? " TL" : " " + currency);

    internal static string LocalTime(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Istanbul).ToString("dd.MM.yyyy HH:mm", Turkish);

    internal sealed record AccountView(int AccountId, string Name, string IbanLast4, string Balance);

    internal sealed record TransactionView(
        int Number, int TransactionId, string Date, string Amount, string Description, string Category, string? CardLast4);

    internal sealed record TransactionList(string? Message, IReadOnlyList<TransactionView> Transactions);

    internal sealed record CardView(int CardId, string Kind, string Last4, string Status);

    internal sealed record CardResult(string? Message, CardView? Card);

    internal sealed record ComplaintResult(string? Message, int? ComplaintId, string? Status);
}
