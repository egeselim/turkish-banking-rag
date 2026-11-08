using System.Diagnostics;
using System.Text.Json;
using Assistant.Core.Audit;
using Assistant.Core.Banking;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Assistant.Core.Chat;

/// <summary>
/// Function middleware for one turn. It audits every tool that runs and is the second approval check: an
/// irreversible tool runs only for a call the customer approved in this turn, with the arguments that were shown.
/// It goes by tool name rather than by <see cref="ApprovalRequiredAIFunction"/>, so forgetting the wrapper on a
/// tool does not open the door.
/// </summary>
internal sealed class ToolGuard(int? customerId, AuditTrail? audit, Dictionary<string, FunctionCallContent> approvedCalls)
{
    public const string DeniedResult = "Bu işlem müşteri onayı olmadan çalıştırılamaz; işlem yapılmadı.";

    public async ValueTask<object?> InvokeAsync(
        AIAgent agent,
        FunctionInvocationContext context,
        Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> next,
        CancellationToken cancellationToken)
    {
        string name = context.Function.Name;
        if (BankingTools.IrreversibleTools.Contains(name) && !TakeApproval(name, context))
        {
            await RecordAsync(name, context, AuditOutcome.Denied, TimeSpan.Zero).ConfigureAwait(false);
            return DeniedResult;
        }

        long started = Stopwatch.GetTimestamp();
        object? result;
        try
        {
            result = await next(context, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await RecordAsync(name, context, AuditOutcome.Failed, Stopwatch.GetElapsedTime(started)).ConfigureAwait(false);
            throw;
        }

        var outcome = ReportsNotFound(result) ? AuditOutcome.NotFound : AuditOutcome.Succeeded;
        await RecordAsync(name, context, outcome, Stopwatch.GetElapsedTime(started)).ConfigureAwait(false);
        return result;
    }

    /// <summary>An approval is spent by the first call that uses it.</summary>
    private bool TakeApproval(string name, FunctionInvocationContext context)
    {
        if (context.CallContent is not { CallId: { } callId } || !approvedCalls.Remove(callId, out var shown))
        {
            return false;
        }

        return shown.Name == name && SameArguments(shown.Arguments, context.Arguments);
    }

    private static bool SameArguments(IDictionary<string, object?>? shown, IDictionary<string, object?>? running) =>
        Serialize(shown) == Serialize(running);

    private static string Serialize(IDictionary<string, object?>? arguments) =>
        JsonSerializer.Serialize(arguments ?? new Dictionary<string, object?>(), AIJsonUtilities.DefaultOptions);

    // Banking tools report "not found" in their result's Message field (BankingTools: CardResult and the like).
    private static bool ReportsNotFound(object? result)
    {
        if (result is null)
        {
            return false;
        }

        var json = result is JsonElement element ? element : JsonSerializer.SerializeToElement(result, AIJsonUtilities.DefaultOptions);
        return json.ValueKind == JsonValueKind.Object && json.EnumerateObject().Any(p =>
            p.Name is "message" or "Message" && p.Value.ValueKind == JsonValueKind.String);
    }

    private Task RecordAsync(string name, FunctionInvocationContext context, AuditOutcome outcome, TimeSpan duration) =>
        audit?.RecordAsync(customerId, name, context.Arguments, outcome, duration) ?? Task.CompletedTask;
}
