using Microsoft.Agents.AI;

namespace Assistant.Core.Chat;

/// <summary>
/// One conversation, bound to at most one demo customer for its whole lifetime. Switching customers means a new
/// session, so one customer's earlier tool results never sit in the history next to another customer's tools.
/// </summary>
public sealed class AssistantSession
{
    internal AssistantSession(AgentSession agentSession, int? customerId)
    {
        AgentSession = agentSession;
        CustomerId = customerId;
    }

    /// <summary>Set by the server (the user's choice in the UI), never by the model.</summary>
    public int? CustomerId { get; }

    internal AgentSession AgentSession { get; }
}
