using System.Runtime.CompilerServices;
using Assistant.Rag.Search;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Assistant.Core.Chat;

/// <param name="Sources">Passages given to the model for this turn; the answer cites them by number.</param>
/// <param name="Text">The answer, streamed as text fragments.</param>
public sealed record AssistantTurn(IReadOnlyList<CitedSource> Sources, IAsyncEnumerable<string> Text);

/// <summary>
/// The RAG chat assistant. Each turn retrieves sources for the question and hands them to the agent as
/// per-run instructions: they apply to this turn only and are not stored in the conversation history
/// (unlike AIContext.Messages), so the history stays small while every answer sees fresh sources.
/// </summary>
public sealed class BankAssistant
{
    public const int SourceCount = 5;

    private readonly ChatClientAgent _agent;
    private readonly IDocumentRetriever _retriever;

    public BankAssistant(IChatClient chatClient, IDocumentRetriever retriever, ILoggerFactory? loggerFactory = null)
    {
        _retriever = retriever;
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
    public ValueTask<AgentSession> CreateSessionAsync(CancellationToken cancellationToken = default) =>
        _agent.CreateSessionAsync(cancellationToken);

    public async Task<AssistantTurn> AskAsync(AgentSession session, string question, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        var hits = await _retriever.SearchAsync(question, SourceCount, RetrievalMode.Hybrid, cancellationToken).ConfigureAwait(false);
        var sources = hits.Select((h, i) => CitedSource.From(i + 1, h.Chunk)).ToList();

        var runOptions = new ChatClientAgentRunOptions(new ChatOptions { Instructions = AssistantPrompts.FormatSources(sources) });
        return new AssistantTurn(sources, StreamAsync(session, question, runOptions, cancellationToken));
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
