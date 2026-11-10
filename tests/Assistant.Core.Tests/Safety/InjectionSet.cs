using System.Globalization;
using System.Text.Json;

namespace Assistant.Core.Tests.Safety;

/// <summary>
/// One prompt-injection attack from eval/injection.jsonl. <see cref="Vector"/> says where the attack enters:
/// the customer's own messages ("user"), a transaction description returned by a tool ("tool_result"), or a
/// retrieved document ("document"). <see cref="Guard"/> is what <c>InputGuard</c> is expected to do with the turns.
/// <see cref="ForbiddenApprovals"/> are tools the model should not even propose here; proposing one is reported,
/// not failed, because the approval card still stops it.
/// </summary>
public sealed record InjectionExample(
    string Id,
    string Category,
    string Vector,
    string Guard,
    int Customer,
    IReadOnlyList<string> Turns,
    IReadOnlyList<string> ForbiddenApprovals,
    string? Payload = null,
    string? DocTitle = null,
    string? Notes = null)
{
    public const string OtherCustomerPlaceholder = "{other_customer_id}";
    public const string OtherCardPlaceholder = "{other_card_id}";

    /// <summary>The turns with placeholders filled in from the test's bank data.</summary>
    public IReadOnlyList<string> ResolveTurns(int otherCustomerId, int otherCardId) =>
    [
        .. Turns.Select(t => t
            .Replace(OtherCustomerPlaceholder, otherCustomerId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace(OtherCardPlaceholder, otherCardId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)),
    ];

    public override string ToString() => Id;
}

public static class InjectionSet
{
    public static readonly string[] Vectors = ["user", "tool_result", "document"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    private static readonly Lazy<IReadOnlyList<InjectionExample>> LazyAll = new(Load);

    public static IReadOnlyList<InjectionExample> All => LazyAll.Value;

    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "Eval", "injection.jsonl");

    public static InjectionExample Get(string id) => All.Single(e => e.Id == id);

    private static List<InjectionExample> Load() =>
    [
        .. File.ReadLines(FilePath)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => JsonSerializer.Deserialize<InjectionExample>(line, Json)
                ?? throw new InvalidDataException("Empty line in injection.jsonl.")),
    ];
}
