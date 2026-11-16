using System.Globalization;
using Assistant.Core.Chat;
using Assistant.Core.Models;
using Assistant.Core.Retrieval;
using Assistant.Rag.Search;
using Eval.Runner;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Qdrant.Client;
using Testcontainers.Qdrant;

// Eval runner (step 3.2): retrieval mode. Uses the app's own composition (AddNimModels + AddRag), with the
// on-disk embedding cache switched on, so a run without an API key measures exactly what the app searches.

// Answer mode: above this share of failed questions the run is not written as the baseline.
const double MaxErrorShare = 0.05;

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
        .Configure<RagOptions>(configuration.GetSection(RagOptions.SectionName)) // the app's Rag:Mode and Rag:RerankModel
        .BuildServiceProvider();
    var rag = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<RagOptions>>().Value;

    var chunks = SourceDocuments.LoadChunks(Path.Combine(root, "docs", "sources"));
    await services.GetRequiredService<ChunkIndex>().SyncAsync(chunks, cts.Token);

    var questions = QuestionSet.Load(Path.Combine(root, "eval", "questions.jsonl"))
        .Where(q => options.Subset == "full" || q.Smoke)
        .Take(options.Limit ?? int.MaxValue)
        .ToList();
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{chunks.Count} chunk yüklendi; {questions.Count} soru ölçülüyor ({options.Subset}, {options.Mode})..."));

    string reportsDir = Path.Combine(root, "eval", "reports");
    string? comparePath = options.NoCompare ? null : options.Compare ?? Path.Combine(reportsDir, options.BaselineName + ".json");
    if (options.Compare is not null && !File.Exists(options.Compare))
    {
        Console.Error.WriteLine($"Karşılaştırma raporu bulunamadı: {options.Compare}");
        return 2;
    }

    string? baselineJson = comparePath is not null && File.Exists(comparePath) ? await File.ReadAllTextAsync(comparePath, cts.Token) : null;
    string? baselineName = baselineJson is null ? null : Path.GetFileName(comparePath);
    var createdUtc = DateTimeOffset.UtcNow;

    if (options.Mode == "answer")
    {
        var mainClient = services.GetRequiredService<IChatClient>();
        var judgeClient = options.Judge == "small"
            ? services.GetRequiredKeyedService<IChatClient>(ModelServiceCollectionExtensions.SmallChatClientKey)
            : mainClient;
        string judgeModel = options.Judge == "small" ? nim.SmallChatModel : nim.ChatModel;
        var assistantMeter = new UsageMeter(mainClient);
        var judgeMeter = new UsageMeter(judgeClient);
        // Exactly what the app searches with: the DI retriever (reranked when Rag:RerankModel is set) and Rag:Mode.
        var assistant = new BankAssistant(assistantMeter, services.GetRequiredService<IDocumentRetriever>(), retrievalMode: rag.Mode);
        var evaluator = new AnswerEvaluator(assistant, assistantMeter, AnswerEvaluator.CreateJudges(), new ChatConfiguration(judgeMeter), judgeMeter);

        var answers = await evaluator.RunAsync(questions, (n, r) => Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"[{n}/{questions.Count}] {r.Id} {string.Join(" ", r.Scores.Select(s => $"{s.Key[..1]}={s.Value?.ToString("F0", CultureInfo.InvariantCulture) ?? "-"}"))}{(r.Refused ? " ret" : "")}{(r.Error is null ? "" : " HATA " + r.Error)} ({r.LatencyMs / 1000:F1} sn)")), cts.Token);

        var answerReport = new AnswerReport(
            AnswerReport.CurrentSchemaVersion,
            createdUtc,
            options.Subset,
            nim.ChatModel,
            judgeModel, // ADR-001 default: the main model
            string.IsNullOrWhiteSpace(rag.RerankModel) ? rag.Mode.ToString() : $"{rag.Mode}+{rag.RerankModel}",
            options.PriceIn is double pin && options.PriceOut is double pout ? new Pricing(pin, pout) : null,
            answers,
            [.. ContactCheck.PhonesIn(chunks.Select(c => c.Text))]);
        var answerBaseline = baselineJson is null ? null : AnswerReport.FromJson(baselineJson);
        int errors = answers.Count(a => a.Error is not null);
        bool tooManyErrors = answers.Count > 0 && (double)errors / answers.Count > MaxErrorShare;
        if (tooManyErrors && options.WriteBaseline)
        {
            // An outage must not become the baseline; the run is kept under local/ for inspection.
            Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{errors}/{answers.Count} soruda hata var; baseline yazılmadı, rapor eval/reports/local/ altında."));
            options = options with { WriteBaseline = false };
        }

        await WriteAsync(answerReport.ToJson(), AnswerMarkdown.Render(answerReport, answerBaseline, baselineName));
        return tooManyErrors ? 1 : 0;
    }

    // Base methods on the plain hybrid retriever (never the app's reranked one), plus one pair per --rerank model.
    var hybridRetriever = services.GetRequiredService<HybridRetriever>();
    var rerankers = services.GetRequiredService<RerankerFactory>();
    List<RetrievalMethod> methods = [.. RetrievalEvaluator.BaseMethods(hybridRetriever)];
    foreach (string model in options.RerankModels)
    {
        var reranking = new RerankingRetriever(hybridRetriever, rerankers.Create(model), RetrievalMetrics.CandidateDepth);
        methods.Add(new RetrievalMethod($"VectorOnly+{model}", reranking, RetrievalMode.VectorOnly, RetrievalMetrics.TopK));
        methods.Add(new RetrievalMethod($"{RetrievalReport.Hybrid}+{model}", reranking, RetrievalMode.Hybrid, RetrievalMetrics.TopK));
    }

    var results = await RetrievalEvaluator.RunAsync(methods, questions, cts.Token);
    var stats = services.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>().GetService<EmbeddingCacheStats>()!;

    var report = new RetrievalReport(
        RetrievalReport.CurrentSchemaVersion,
        createdUtc,
        options.Subset,
        nim.EmbeddingModel,
        nim.EmbeddingDimensions,
        chunks.Count,
        stats.Hits,
        stats.Added,
        results);
    var baseline = baselineJson is null ? null : RetrievalReport.FromJson(baselineJson);
    await WriteAsync(report.ToJson(), MarkdownReport.Render(report, baseline, baselineName));

    async Task WriteAsync(string json, string markdown)
    {
        string outDir = options.WriteBaseline ? reportsDir : options.OutDir ?? Path.Combine(reportsDir, "local");
        string name = options.WriteBaseline
            ? options.BaselineName
            : string.Create(CultureInfo.InvariantCulture, $"{options.Mode}-{options.Subset}-{createdUtc:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(outDir);
        await File.WriteAllTextAsync(Path.Combine(outDir, name + ".json"), json.ReplaceLineEndings("\n"), cts.Token);
        await File.WriteAllTextAsync(Path.Combine(outDir, name + ".md"), markdown.ReplaceLineEndings("\n"), cts.Token);

        int sectionEnd = markdown.IndexOf("\n## Zayıf", StringComparison.Ordinal) is int z and >= 0 ? z
            : markdown.IndexOf("\n## ", StringComparison.Ordinal) is int s and >= 0 && options.Mode == "retrieval" ? s
            : markdown.Length;
        Console.WriteLine();
        Console.WriteLine(markdown[..sectionEnd]);
        Console.WriteLine($"\nRapor: {Path.GetRelativePath(root, Path.Combine(outDir, name + ".md"))}");
    }

    if (options.FailUnderHit5 is null && !options.FailOnRegression)
    {
        return 0;
    }

    // The gate checks the method the app actually searches with (Rag:Mode / Rag:RerankModel), not a fixed one.
    string appMethod = RegressionGate.AppMethod(rag);
    var failures = RegressionGate.Check(report, options.FailOnRegression ? baseline : null, appMethod, options.FailUnderHit5);
    if (options.FailOnRegression && baseline is null)
    {
        failures = [.. failures, "Karşılaştırılacak baseline bulunamadı (--fail-on-regression)."];
    }

    foreach (string failure in failures)
    {
        Console.Error.WriteLine("KAPI: " + failure);
    }

    Console.WriteLine(failures.Count == 0 ? $"Kapı geçti ({appMethod})." : "");
    return failures.Count == 0 ? 0 : 1;
}
catch (DotNet.Testcontainers.Builders.DockerUnavailableException)
{
    Console.Error.WriteLine("Docker çalışmıyor: Qdrant başlatılamadı. Docker Desktop'ı açın ya da var olan bir Qdrant için --qdrant <url> verin.");
    return 2;
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
