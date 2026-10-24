using System.Globalization;
using System.Text.Json;
using Assistant.Privacy;
using Microsoft.Extensions.AI;

namespace Assistant.Core.Safety;

/// <summary>
/// Masks PII in everything sent to the model: message text, reasoning, tool-call arguments,
/// tool results and instructions. It sits directly above the raw provider client, *inside*
/// function invocation, so tool results added during the tool loop are masked too.
/// </summary>
/// <remarks>
/// Messages are copied, never mutated, so the caller's history keeps the original text.
/// <see cref="ChatMessage.RawRepresentation"/> and <see cref="AIContent.RawRepresentation"/> are
/// not copied: a provider may send a raw representation verbatim, bypassing the masked text.
/// </remarks>
internal sealed class PiiMaskingChatClient(IChatClient innerClient, IPiiMasker masker) : DelegatingChatClient(innerClient)
{
    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        base.GetResponseAsync(MaskMessages(messages), MaskOptions(options), cancellationToken);

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        base.GetStreamingResponseAsync(MaskMessages(messages), MaskOptions(options), cancellationToken);

    private List<ChatMessage> MaskMessages(IEnumerable<ChatMessage> messages) =>
        [.. messages.Select(m => new ChatMessage(m.Role, [.. m.Contents.Select(MaskContent)])
        {
            AuthorName = m.AuthorName,
            MessageId = m.MessageId,
            CreatedAt = m.CreatedAt,
            AdditionalProperties = m.AdditionalProperties,
        })];

    private AIContent MaskContent(AIContent content) => content switch
    {
        TextContent text => new TextContent(masker.Mask(text.Text ?? "")),
        TextReasoningContent reasoning => new TextReasoningContent(masker.Mask(reasoning.Text ?? "")),
        FunctionCallContent call => new FunctionCallContent(call.CallId, call.Name, MaskArguments(call.Arguments)),
        FunctionResultContent result => new FunctionResultContent(result.CallId, MaskValue(result.Result)),

        // Other content types (approval requests, usage, etc.) carry no free text from users or tools.
        _ => content,
    };

    private Dictionary<string, object?>? MaskArguments(IDictionary<string, object?>? arguments) =>
        arguments?.ToDictionary(a => a.Key, a => MaskValue(a.Value));

    // Keeps the original value when it contains no PII, so typed values (numbers, JSON) survive untouched.
    private object? MaskValue(object? value)
    {
        if (value is null)
        {
            return null;
        }

        string text = value switch
        {
            string s => s,
            JsonElement json => json.GetRawText(),
            IConvertible convertible => convertible.ToString(CultureInfo.InvariantCulture),
            _ => JsonSerializer.Serialize(value, AIJsonUtilities.DefaultOptions),
        };

        string masked = masker.Mask(text);
        return ReferenceEquals(masked, text) || masked == text ? value : masked;
    }

    private ChatOptions? MaskOptions(ChatOptions? options)
    {
        if (options?.Instructions is null)
        {
            return options;
        }

        var clone = options.Clone();
        clone.Instructions = masker.Mask(options.Instructions);
        return clone;
    }
}
