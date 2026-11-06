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

    internal AssistantSession(AgentSession agentSession, int? customerId)
    {
        AgentSession = agentSession;
        CustomerId = customerId;
    }

    /// <summary>Set by the server (the user's choice in the UI), never by the model.</summary>
    public int? CustomerId { get; }

    internal AgentSession AgentSession { get; }

    internal void AddPending(ToolApprovalRequestContent request) => _pending[request.RequestId] = request;

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
    /// Agent Framework rejects every unanswered request on the next run (a new message, or an answer to another
    /// request); the local copies are dropped at the same time.
    /// </summary>
    internal void ClearPending() => _pending.Clear();
}
