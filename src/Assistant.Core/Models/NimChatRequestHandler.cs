using System.Text;
using System.Text.Json.Nodes;

namespace Assistant.Core.Models;

/// <summary>
/// Adds <c>"chat_template_kwargs": {"enable_thinking": false}</c> to NIM chat-completion requests when
/// <see cref="NimOptions.DisableThinking"/> is set. On 2026-11-11 the reasoning models spent their whole output
/// budget on reasoning for long RAG prompts and returned empty answers; with thinking off they answer directly.
/// Done at the HTTP level because the OpenAI SDK has no supported field for it. Only chat requests on the chat
/// HttpClient pass through here; embeddings use another client.
/// </summary>
internal sealed class NimChatRequestHandler(Func<bool> disableThinking) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (disableThinking()
            && request.Method == HttpMethod.Post
            && request.RequestUri?.AbsolutePath.EndsWith("/chat/completions", StringComparison.Ordinal) == true
            && request.Content is not null)
        {
            string body = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (JsonNode.Parse(body) is JsonObject json)
            {
                var kwargs = json["chat_template_kwargs"] as JsonObject ?? [];
                kwargs["enable_thinking"] = false;
                json["chat_template_kwargs"] = kwargs;
                var content = new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json");
                request.Content.Dispose();
                request.Content = content;
            }
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
