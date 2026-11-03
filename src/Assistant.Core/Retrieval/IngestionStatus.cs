using Assistant.Rag.Search;

namespace Assistant.Core.Retrieval;

public enum IngestionState
{
    Pending,
    Running,
    Ready,
    Failed,
}

/// <summary>Progress of the startup ingestion, observed by the UI (chat stays disabled until <see cref="IngestionState.Ready"/>).</summary>
public sealed class IngestionStatus
{
    private readonly Lock _gate = new();

    public IngestionState State { get; private set; } = IngestionState.Pending;

    public int ChunkCount { get; private set; }

    public IngestionResult? Result { get; private set; }

    /// <summary>Set when <see cref="State"/> is <see cref="IngestionState.Failed"/>; safe to show (no document content).</summary>
    public string? Error { get; private set; }

    public event Action? Changed;

    internal void SetRunning() => Update(() => State = IngestionState.Running);

    internal void SetReady(int chunkCount, IngestionResult result) => Update(() =>
    {
        State = IngestionState.Ready;
        ChunkCount = chunkCount;
        Result = result;
        Error = null;
    });

    internal void SetFailed(string error) => Update(() =>
    {
        State = IngestionState.Failed;
        Error = error;
    });

    private void Update(Action change)
    {
        lock (_gate)
        {
            change();
        }

        Changed?.Invoke();
    }
}
