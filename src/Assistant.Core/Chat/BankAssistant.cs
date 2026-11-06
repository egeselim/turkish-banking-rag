using System.Runtime.CompilerServices;
using Assistant.Core.Banking;
using Assistant.Rag.Search;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Assistant.Core.Chat;

/// <param name="Sources">Passages given to the model for this turn; the answer cites them by number.</param>
/// <param name="Text">The answer, streamed as text fragments.</param>
public sealed record AssistantTurn(IReadOnlyList<CitedSource> Sources, IAsyncEnumerable<string> Text);

/// <summary>
/// The chat assistant. Each turn retrieves sources for the question and hands them to the agent as per-run
/// instructions: they apply to this turn only and are not stored in the conversation history (unlike
/// AIContext.Messages), so the history stays small while every answer sees fresh sources.
/// When the session has a customer, the turn also gets banking tools bound to that customer; Agent Framework
/// unions per-run tools with the agent's own, and tool calls and results stay in the session history,
/// so a follow-up such as "ikincisi" can refer to an earlier list.
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

    public async Task<AssistantTurn> AskAsync(AssistantSession session, string question, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        var hits = await _retriever.SearchAsync(question, SourceCount, RetrievalMode.Hybrid, cancellationToken).ConfigureAwait(false);
        var sources = hits.Select((h, i) => CitedSource.From(i + 1, h.Chunk)).ToList();

        var chatOptions = new ChatOptions { Instructions = AssistantPrompts.FormatTurn(sources, session.CustomerId is not null) };
        if (session.CustomerId is { } customerId)
        {
            chatOptions.Tools = new BankingTools(_bank!, customerId).Create();
        }

        return new AssistantTurn(sources, StreamAsync(session.AgentSession, question, new ChatClientAgentRunOptions(chatOptions), cancellationToken));
    }

    private async IAsyncEnumerable<string> StreamAsync(
        AgentSession session, string question, AgentRunOptions runOptions, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var update in _agent.RunStreamingAsync(question, session, runOptions, cancellationToken).ConfigureAwait(false))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                yield return update.Text;
            }
        }
    }
}
