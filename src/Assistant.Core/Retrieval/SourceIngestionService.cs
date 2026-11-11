using Assistant.Rag.Chunking;
using Assistant.Rag.Documents;
using Assistant.Rag.Search;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistant.Core.Retrieval;

public sealed class RagOptions
{
    public const string SectionName = "Rag";

    /// <summary>Folder with the source PDFs (docs/sources); the AppHost sets it.</summary>
    public string SourcesPath { get; set; } = "";
}

/// <summary>
/// Reads the source PDFs at startup and syncs them into the index. Idempotent (see <see cref="ChunkIndex"/>),
/// so restarts re-embed nothing unless the documents changed. Logs counts only, never document text.
/// </summary>
internal sealed partial class SourceIngestionService(
    ChunkIndex index,
    IngestionStatus status,
    IOptions<RagOptions> options,
    ILogger<SourceIngestionService> logger) : BackgroundService
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            status.SetRunning();
            try
            {
                var chunks = LoadChunks(options.Value.SourcesPath);
                var result = await index.SyncAsync(chunks, stoppingToken).ConfigureAwait(false);
                status.SetReady(chunks.Count, result);
                LogIngested(chunks.Count, result.Added, result.Unchanged, result.Removed);
                return;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                LogAttemptFailed(ex, attempt, MaxAttempts);
                if (attempt == MaxAttempts)
                {
                    status.SetFailed("Dokümanlar yüklenemedi. Ayrıntılar sunucu logunda.");
                    return;
                }

                await Task.Delay(RetryDelay, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    internal static List<DocumentChunk> LoadChunks(string sourcesPath) => SourceDocuments.LoadChunks(sourcesPath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Source ingestion done: {Chunks} chunks ({Added} added, {Unchanged} unchanged, {Removed} removed)")]
    private partial void LogIngested(int chunks, int added, int unchanged, int removed);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Source ingestion attempt {Attempt}/{MaxAttempts} failed")]
    private partial void LogAttemptFailed(Exception exception, int attempt, int maxAttempts);
}
