using Assistant.Core.Audit;
using Assistant.Core.Banking;
using Assistant.Core.Retrieval;
using Microsoft.Extensions.Options;
using Assistant.Rag.Search;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Assistant.Core.Chat;

public static class ChatServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="BankAssistant"/> over the (PII-masking) main chat client and the retriever.
    /// Banking tools are available when <see cref="SelimBankClient"/> and <see cref="AuditTrail"/> are registered
    /// (AddSelimBankClient, AddAuditLog).
    /// </summary>
    public static IServiceCollection AddBankAssistant(this IServiceCollection services) =>
        services.AddSingleton(sp => new BankAssistant(
            sp.GetRequiredService<IChatClient>(),
            sp.GetRequiredService<IDocumentRetriever>(),
            sp.GetService<ILoggerFactory>(),
            sp.GetService<SelimBankClient>(),
            sp.GetService<AuditTrail>(),
            sp.GetService<IOptions<RagOptions>>()?.Value.Mode ?? RetrievalMode.Hybrid));
}
