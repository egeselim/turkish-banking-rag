using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Assistant.Core.Chat;

/// <summary>A tool call waiting for the customer's decision. <see cref="Summary"/> is built from the call's real arguments.</summary>
public sealed record PendingApproval(string Id, string ToolName, string Summary);

/// <summary>
/// One conversation, bound to at most one demo customer for its whole lifetime. Switching customers means a new
/// session, so one customer's earlier tool results never sit in the history next to another customer's tools.
/// It also keeps the approval requests surfaced in the last turn server-side; the UI only sends back an id
/// and a decision.
/// </summary>
public sealed class AssistantSession
{
    private readonly Dictionary<string, ToolApprovalRequestContent> _pending = [];

    // Everything surfaced in the last turn, answered or not; the second approval check (ToolGuard) compares against it.
    private readonly Dictionary<string, ToolApprovalRequestContent> _issued = [];

    internal AssistantSession(AgentSession agentSession, int? customerId)
    {
        AgentSession = agentSession;
        CustomerId = customerId;
    }

    /// <summary>Set by the server (the user's choice in the UI), never by the model.</summary>
    public int? CustomerId { get; }

    internal AgentSession AgentSession { get; }

    internal void AddPending(ToolApprovalRequestContent request)
    {
        _pending[request.RequestId] = request;
        _issued[request.RequestId] = request;
    }

    /// <summary>A request can be answered once; afterwards (or for an unknown id) this returns false.</summary>
    internal bool TryTakePending(string id, out ToolApprovalRequestContent request)
    {
        if (_pending.Remove(id, out var found))
        {
            request = found;
            return true;
        }

        request = null!;
        return false;
    }

    /// <summary>
    /// Starts a new run. Agent Framework rejects every unanswered request on the next run (a new message, or an answer
    /// to another request), so all requests end here. Returns the calls approved by <paramref name="message"/> (only for
    /// requests issued in this session, with the arguments shown, keyed by call id) and the requests left unanswered.
    /// </summary>
    internal (Dictionary<string, FunctionCallContent> Approved, IReadOnlyList<ToolApprovalRequestContent> Abandoned) BeginRun(ChatMessage message)
    {
        var approved = new Dictionary<string, FunctionCallContent>();
        foreach (var response in message.Contents.OfType<ToolApprovalResponseContent>().Where(r => r.Approved))
        {
            if (_issued.TryGetValue(response.RequestId, out var issued) && issued.ToolCall is FunctionCallContent call)
            {
                approved[call.CallId] = call;
            }
        }

        // A request answered through the message (below the UI layer) is not abandoned either.
        var answered = message.Contents.OfType<ToolApprovalResponseContent>().Select(r => r.RequestId).ToHashSet();
        IReadOnlyList<ToolApprovalRequestContent> abandoned = [.. _pending.Values.Where(r => !answered.Contains(r.RequestId))];

        _pending.Clear();
        _issued.Clear();
        return (approved, abandoned);
    }
}
