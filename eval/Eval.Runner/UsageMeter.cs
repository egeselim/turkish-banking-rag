using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Eval.Runner;

internal sealed record TokenUsage(long Input, long Output, int Calls)
{
    public static readonly TokenUsage None = new(0, 0, 0);

    public TokenUsage Add(TokenUsage other) => new(Input + other.Input, Output + other.Output, Calls + other.Calls);
}

/// <summary>
/// Counts the tokens the model reports, for streaming and non-streaming calls. Questions run one at a time, so
/// <see cref="Take"/> after each question gives that question's usage. Calls whose provider reports no usage are
/// still counted in <see cref="TokenUsage.Calls"/>.
/// </summary>
internal sealed class UsageMeter(IChatClient innerClient) : DelegatingChatClient(innerClient)
{
    private readonly Lock _lock = new();
    private TokenUsage _usage = TokenUsage.None;

    public TokenUsage Take()
    {
        lock (_lock)
        {
            var usage = _usage;
            _usage = TokenUsage.None;
            return usage;
        }
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        Record(response.Usage);
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        UsageDetails? usage = null;
        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
        {
            foreach (var content in update.Contents.OfType<UsageContent>())
            {
                usage ??= new UsageDetails();
                usage.Add(content.Details);
            }

            yield return update;
        }

        Record(usage);
    }

    private void Record(UsageDetails? usage)
    {
        lock (_lock)
        {
            _usage = _usage.Add(new TokenUsage(usage?.InputTokenCount ?? 0, usage?.OutputTokenCount ?? 0, 1));
        }
    }
}
