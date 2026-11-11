using System.Globalization;
using Assistant.Core.Models;
using Assistant.Core.Retrieval;
using Assistant.Rag.Search;
using Eval.Runner;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Qdrant.Client;
using Testcontainers.Qdrant;

// Eval runner (step 3.2): retrieval mode. Uses the app's own composition (AddNimModels + AddRag), with the
// on-disk embedding cache switched on, so a run without an API key measures exactly what the app searches.

RunnerOptions options;
try
{
    options = RunnerOptions.Parse(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine(RunnerOptions.Usage);
    return 2;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

string root = RepoPaths.Root();
var configuration = RepoPaths.Configuration(root, options.Offline);
var nim = configuration.GetSection(NimOptions.SectionName).Get<NimOptions>() ?? new NimOptions();

QdrantContainer? container = null;
try
{
    Uri qdrantUri;
    if (options.Qdrant is null)
    {
        Console.WriteLine("Geçici Qdrant başlatılıyor (Docker)...");
        container = new QdrantBuilder(RepoPaths.QdrantImage).Build();
        await container.StartAsync(cts.Token);
        qdrantUri = new Uri(container.GetGrpcConnectionString());
    }
    else
    {
        qdrantUri = options.Qdrant;
    }

    await using var services = new ServiceCollection()
        .AddNimModels(configuration)
        .AddSingleton(new QdrantClient(qdrantUri))
        .AddRag()
        .BuildServiceProvider();

    var chunks = SourceDocuments.LoadChunks(Path.Combine(root, "docs", "sources"));
    await services.GetRequiredService<ChunkIndex>().SyncAsync(chunks, cts.Token);

    var questions = QuestionSet.Load(Path.Combine(root, "eval", "questions.jsonl"))
        .Where(q => options.Subset == "full" || q.Smoke)
        .ToList();
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{chunks.Count} chunk yüklendi; {questions.Count} soru ölçülüyor ({options.Subset})..."));

    var results = await RetrievalEvaluator.RunAsync(services.GetRequiredService<IDocumentRetriever>(), questions, cts.Token);
    var stats = services.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>().GetService<EmbeddingCacheStats>()!;

    var report = new RetrievalReport(
        RetrievalReport.CurrentSchemaVersion,
        DateTimeOffset.UtcNow,
        options.Subset,
        nim.EmbeddingModel,
        nim.EmbeddingDimensions,
        chunks.Count,
        stats.Hits,
        stats.Added,
        results);

    string reportsDir = Path.Combine(root, "eval", "reports");
    string? comparePath = options.NoCompare ? null : options.Compare ?? Path.Combine(reportsDir, "baseline-retrieval.json");
    RetrievalReport? baseline = comparePath is not null && File.Exists(comparePath)
        ? RetrievalReport.FromJson(await File.ReadAllTextAsync(comparePath, cts.Token))
        : null;
    if (options.Compare is not null && baseline is null)
    {
        Console.Error.WriteLine($"Karşılaştırma raporu bulunamadı: {options.Compare}");
        return 2;
    }

    string outDir = options.WriteBaseline ? reportsDir : options.OutDir ?? Path.Combine(reportsDir, "local");
    string name = options.WriteBaseline
        ? "baseline-retrieval"
        : string.Create(CultureInfo.InvariantCulture, $"retrieval-{options.Subset}-{report.CreatedUtc:yyyyMMdd-HHmmss}");
    Directory.CreateDirectory(outDir);
    string markdown = MarkdownReport.Render(report, baseline, comparePath is null ? null : Path.GetFileName(comparePath));
    await File.WriteAllTextAsync(Path.Combine(outDir, name + ".json"), report.ToJson().ReplaceLineEndings("\n"), cts.Token);
    await File.WriteAllTextAsync(Path.Combine(outDir, name + ".md"), markdown.ReplaceLineEndings("\n"), cts.Token);

    Console.WriteLine();
    Console.WriteLine(markdown[..markdown.IndexOf("\n## ", StringComparison.Ordinal)]);
    Console.WriteLine($"\nRapor: {Path.GetRelativePath(root, Path.Combine(outDir, name + ".md"))}");

    var hybrid = report.Summaries().Single(s => s.Mode == RetrievalMode.Hybrid);
    double hit5 = hybrid.Questions == 0 ? 0 : (double)hybrid.Hit5 / hybrid.Questions;
    if (options.FailUnderHit5 is double floor && hit5 < floor)
    {
        Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Hybrid Hit@5 {hit5:P0}, eşik {floor:P0} altında."));
        return 1;
    }

    return 0;
}
catch (OperationCanceledException) when (cts.IsCancellationRequested)
{
    Console.Error.WriteLine("İptal edildi.");
    return 130;
}
catch (InvalidOperationException ex)
{
    // Expected failures (cache miss offline, missing configuration). The message never contains the API key.
    Console.Error.WriteLine(ex.Message);
    return 1;
}
finally
{
    if (container is not null)
    {
        await container.DisposeAsync();
    }
}
