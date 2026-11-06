using System.Runtime.CompilerServices;
using Assistant.Core.Banking;
using Assistant.Rag.Search;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Assistant.Core.Chat;

/// <param name="Sources">Passages given to the model for this turn; the answer cites them by number.</param>
/// <param name="Text">The answer, streamed as text fragments.</param>
/// <param name="Approvals">Tool calls waiting for the customer's decision. Complete once <paramref name="Text"/> has been read to the end.</param>
public sealed record AssistantTurn(
    IReadOnlyList<CitedSource> Sources, IAsyncEnumerable<string> Text, IReadOnlyList<PendingApproval> Approvals);

/// <summary>
/// The chat assistant. Each turn retrieves sources for the question and hands them to the agent as per-run
/// instructions: they apply to this turn only and are not stored in the conversation history (unlike
/// AIContext.Messages), so the history stays small while every answer sees fresh sources.
/// When the session has a customer, the turn also gets banking tools bound to that customer; Agent Framework
/// unions per-run tools with the agent's own, and tool calls and results stay in the session history,
/// so a follow-up such as "ikincisi" can refer to an earlier list.
/// Irreversible tools stop the turn with approval requests; <see cref="RespondAsync"/> continues it.
/// </summary>
public sealed class BankAssistant
{
    public const int SourceCount = 5;

    private readonly ChatClientAgent _agent;
    private readonly IDocumentRetriever _retriever;
    private readonly SelimBankClient? _bank;

    public BankAssistant(
        IChatClient chatClient, IDocumentRetriever retriever, ILoggerFactory? loggerFactory = null, SelimBankClient? bank = null)
    {
        _retriever = retriever;
        _bank = bank;

        // Approval-response binding (on by default, DisableApprovalResponseBinding = false) records every approval
        // request the framework surfaces in the session and honours only those, once, with the surfaced arguments.
        _agent = new ChatClientAgent(
            chatClient,
            new ChatClientAgentOptions
            {
                Name = "SelimBankAsistan",
                ChatOptions = new ChatOptions { Instructions = AssistantPrompts.System, Temperature = 0.2f, MaxOutputTokens = 1500 },
            },
            loggerFactory);
    }

    /// <summary>A conversation. Keep it for follow-up questions ("ikincisi ne?") to see earlier turns.</summary>
    /// <param name="customerId">The demo customer chosen in the UI, or <see langword="null"/> for document questions only.</param>
    public async ValueTask<AssistantSession> CreateSessionAsync(int? customerId = null, CancellationToken cancellationToken = default)
    {
        if (customerId is not null && _bank is null)
        {
            throw new InvalidOperationException("A customer session needs SelimBankClient (AddSelimBankClient).");
        }

        return new AssistantSession(await _agent.CreateSessionAsync(cancellationToken).ConfigureAwait(false), customerId);
    }

    /// <summary>A new question. Any approval still pending from the previous turn is rejected by Agent Framework.</summary>
    public async Task<AssistantTurn> AskAsync(AssistantSession session, string question, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        var hits = await _retriever.SearchAsync(question, SourceCount, RetrievalMode.Hybrid, cancellationToken).ConfigureAwait(false);
        var sources = hits.Select((h, i) => CitedSource.From(i + 1, h.Chunk)).ToList();

        return Run(session, new ChatMessage(ChatRole.User, question), AssistantPrompts.FormatTurn(sources, session.CustomerId is not null), sources, cancellationToken);
    }

    /// <summary>
    /// The customer's decision on a pending approval. Unknown or already answered ids are refused here; Agent
    /// Framework additionally ignores any approval it did not issue. Other requests from the same turn are rejected.
    /// </summary>
    public Task<AssistantTurn> RespondAsync(AssistantSession session, string approvalId, bool approve, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!session.TryTakePending(approvalId, out var request))
        {
            throw new InvalidOperationException("This approval is unknown or has already been answered.");
        }

        var response = request.CreateResponse(approve, approve ? null : "Müşteri işlemi reddetti.");
        return Task.FromResult(RunMessage(session, new ChatMessage(ChatRole.User, [response]), cancellationToken));
    }

    /// <summary>Sends a raw message on the session (approval responses). Internal so tests can try forged approvals.</summary>
    internal AssistantTurn RunMessage(AssistantSession session, ChatMessage message, CancellationToken cancellationToken) =>
        Run(session, message, AssistantPrompts.ApprovalTurn, [], cancellationToken);

    private AssistantTurn Run(
        AssistantSession session, ChatMessage message, string instructions, IReadOnlyList<CitedSource> sources, CancellationToken cancellationToken)
    {
        session.ClearPending();

        var chatOptions = new ChatOptions { Instructions = instructions };
        BankingTools? tools = null;
        if (session.CustomerId is { } customerId)
        {
            tools = new BankingTools(_bank!, customerId);
            chatOptions.Tools = tools.Create();
        }

        var approvals = new List<PendingApproval>();
        return new AssistantTurn(
            sources,
            StreamAsync(session, message, new ChatClientAgentRunOptions(chatOptions), tools, approvals, cancellationToken),
            approvals);
    }

    private async IAsyncEnumerable<string> StreamAsync(
        AssistantSession session, ChatMessage message, AgentRunOptions runOptions, BankingTools? tools,
        List<PendingApproval> approvals, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var requests = new List<ToolApprovalRequestContent>();
        await foreach (var update in _agent.RunStreamingAsync(message, session.AgentSession, runOptions, cancellationToken).ConfigureAwait(false))
        {
            requests.AddRange(update.Contents.OfType<ToolApprovalRequestContent>());
            if (!string.IsNullOrEmpty(update.Text))
            {
                yield return update.Text;
            }
        }

        foreach (var request in requests.DistinctBy(r => r.RequestId))
        {
            var call = request.ToolCall as FunctionCallContent;
            string summary = call is not null && tools is not null
                ? await tools.DescribeAsync(call, cancellationToken).ConfigureAwait(false)
                : "Bilinmeyen bir işlem onay bekliyor.";
            session.AddPending(request);
            approvals.Add(new PendingApproval(request.RequestId, call?.Name ?? "", summary));
        }
    }
}
