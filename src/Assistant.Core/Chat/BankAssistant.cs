using System.Runtime.CompilerServices;
using Assistant.Core.Audit;
using Assistant.Core.Banking;
using Assistant.Core.Safety;
using Assistant.Privacy;
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
/// Every run goes through two middlewares built for that turn: <see cref="ToolGuard"/> (audit, second approval check)
/// and an output filter that masks PII in the answer. The input guard runs first, in <see cref="AskAsync"/>, because
/// retrieval already sends the question to the embedding model; a blocked message never reaches either model.
/// </summary>
public sealed class BankAssistant
{
    public const int SourceCount = 5;

    private readonly ChatClientAgent _agent;
    private readonly IDocumentRetriever _retriever;
    private readonly SelimBankClient? _bank;
    private readonly AuditTrail? _audit;
    private readonly PiiMasker _outputMasker = new();
    private readonly RetrievalMode _retrievalMode;

    public BankAssistant(
        IChatClient chatClient, IDocumentRetriever retriever, ILoggerFactory? loggerFactory = null, SelimBankClient? bank = null, AuditTrail? audit = null,
        RetrievalMode retrievalMode = RetrievalMode.Hybrid)
    {
        _retriever = retriever;
        _retrievalMode = retrievalMode;
        _bank = bank;
        _audit = audit;

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

    /// <summary>Which tools a customer turn gets. Tests replace it to prove that <see cref="ToolGuard"/> works on its own.</summary>
    internal Func<BankingTools, IList<AITool>> ToolFactory { get; set; } = tools => tools.Create();

    /// <summary>A conversation. Keep it for follow-up questions ("ikincisi ne?") to see earlier turns.</summary>
    /// <param name="customerId">The demo customer chosen in the UI, or <see langword="null"/> for document questions only.</param>
    public async ValueTask<AssistantSession> CreateSessionAsync(int? customerId = null, CancellationToken cancellationToken = default)
    {
        if (customerId is not null && _bank is null)
        {
            throw new InvalidOperationException("A customer session needs SelimBankClient (AddSelimBankClient).");
        }

        // Tools that run without leaving a trace are not acceptable, so this fails at the start rather than silently.
        if (customerId is not null && _audit is null)
        {
            throw new InvalidOperationException("A customer session needs AuditTrail (AddAuditLog).");
        }

        return new AssistantSession(await _agent.CreateSessionAsync(cancellationToken).ConfigureAwait(false), customerId);
    }

    /// <summary>A new question. Any approval still pending from the previous turn is rejected by Agent Framework.</summary>
    public async Task<AssistantTurn> AskAsync(AssistantSession session, string question, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        var verdict = InputGuard.Check(question);
        if (verdict != InputVerdict.Allowed)
        {
            return await BlockAsync(session, verdict).ConfigureAwait(false);
        }

        var hits = await _retriever.SearchAsync(question, SourceCount, _retrievalMode, cancellationToken).ConfigureAwait(false);
        var sources = hits.Select((h, i) => CitedSource.From(i + 1, h.Chunk)).ToList();

        return Run(session, new ChatMessage(ChatRole.User, question), AssistantPrompts.FormatTurn(sources, session.CustomerId is not null), sources, cancellationToken);
    }

    /// <summary>
    /// The customer's decision on a pending approval. Unknown or already answered ids are refused here; Agent
    /// Framework additionally ignores any approval it did not issue. Other requests from the same turn are rejected.
    /// </summary>
    public async Task<AssistantTurn> RespondAsync(AssistantSession session, string approvalId, bool approve, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!session.TryTakePending(approvalId, out var request))
        {
            throw new InvalidOperationException("This approval is unknown or has already been answered.");
        }

        if (!approve)
        {
            await RecordUnrunAsync(session, request, AuditOutcome.Rejected).ConfigureAwait(false);
        }

        var response = request.CreateResponse(approve, approve ? null : "Müşteri işlemi reddetti.");
        return RunMessage(session, new ChatMessage(ChatRole.User, [response]), cancellationToken);
    }

    /// <summary>Sends a raw message on the session (approval responses). Internal so tests can try forged approvals.</summary>
    internal AssistantTurn RunMessage(AssistantSession session, ChatMessage message, CancellationToken cancellationToken) =>
        Run(session, message, AssistantPrompts.ApprovalTurn, [], cancellationToken);

    private AssistantTurn Run(
        AssistantSession session, ChatMessage message, string instructions, IReadOnlyList<CitedSource> sources, CancellationToken cancellationToken)
    {
        var (approved, abandoned) = session.BeginRun(message);

        var chatOptions = new ChatOptions { Instructions = instructions };
        BankingTools? tools = null;
        if (session.CustomerId is { } customerId)
        {
            tools = new BankingTools(_bank!, customerId);
            chatOptions.Tools = ToolFactory(tools);
        }

        var approvals = new List<PendingApproval>();
        return new AssistantTurn(
            sources,
            StreamAsync(session, message, new ChatClientAgentRunOptions(chatOptions), tools, approved, abandoned, approvals, cancellationToken),
            approvals);
    }

    private async IAsyncEnumerable<string> StreamAsync(
        AssistantSession session, ChatMessage message, AgentRunOptions runOptions, BankingTools? tools,
        Dictionary<string, FunctionCallContent> approved, IReadOnlyList<ToolApprovalRequestContent> abandoned,
        List<PendingApproval> approvals, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var request in abandoned)
        {
            await RecordUnrunAsync(session, request, AuditOutcome.Cancelled).ConfigureAwait(false);
        }

        var guard = new ToolGuard(session.CustomerId, _audit, approved);
        var agent = _agent.AsBuilder()
            .Use(runFunc: null, runStreamingFunc: MaskOutputAsync)
            .Use(guard.InvokeAsync)
            .Build();

        var requests = new List<ToolApprovalRequestContent>();
        await foreach (var update in agent.RunStreamingAsync(message, session.AgentSession, runOptions, cancellationToken).ConfigureAwait(false))
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
            // The summary quotes model-written text (a complaint description), so it gets the same output masking.
            approvals.Add(new PendingApproval(request.RequestId, call?.Name ?? "", _outputMasker.Mask(summary)));
        }
    }

    /// <summary>Output guard (agent-run middleware): the customer never sees PII, even if the model produces it.</summary>
    private async IAsyncEnumerable<AgentResponseUpdate> MaskOutputAsync(
        IEnumerable<ChatMessage> messages, AgentSession? session, AgentRunOptions? options, AIAgent inner,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var filter = new PiiStreamFilter(_outputMasker);
        await foreach (var update in inner.RunStreamingAsync(messages, session, options, cancellationToken).ConfigureAwait(false))
        {
            if (update.Contents.Any(c => c is TextContent))
            {
                string visible = filter.Push(update.Text);
                List<AIContent> contents = [.. update.Contents.Where(c => c is not TextContent)];
                if (visible.Length > 0)
                {
                    contents.Add(new TextContent(visible));
                }

                update.Contents = contents; // a new list: the framework's own copy for the history stays as it was
            }

            yield return update;
        }

        string rest = filter.Flush();
        if (rest.Length > 0)
        {
            yield return new AgentResponseUpdate(ChatRole.Assistant, rest);
        }
    }

    /// <summary>A blocked message gets a fixed answer and never enters the conversation; only the rule goes to the audit log.</summary>
    private async Task<AssistantTurn> BlockAsync(AssistantSession session, InputVerdict verdict)
    {
        (string rule, string message) = verdict == InputVerdict.TooLong
            ? ("too-long", AssistantPrompts.InputTooLongMessage)
            : ("injection", AssistantPrompts.InputBlockedMessage);

        if (_audit is not null)
        {
            await _audit.RecordAsync(
                session.CustomerId, AuditEntryNames.InputGuard, [new("rule", rule)], AuditOutcome.InputBlocked).ConfigureAwait(false);
        }

        return new AssistantTurn([], SingleAsync(message), []);
    }

#pragma warning disable CS1998 // An async iterator with nothing to await: the turn's text is one fixed message.
    private static async IAsyncEnumerable<string> SingleAsync(string text)
    {
        yield return text;
    }
#pragma warning restore CS1998

    // Rejected and cancelled calls never reach the function middleware, so they are recorded here.
    private Task RecordUnrunAsync(AssistantSession session, ToolApprovalRequestContent request, AuditOutcome outcome)
    {
        var call = request.ToolCall as FunctionCallContent;
        return _audit?.RecordAsync(session.CustomerId, call?.Name ?? "", call?.Arguments, outcome) ?? Task.CompletedTask;
    }
}
