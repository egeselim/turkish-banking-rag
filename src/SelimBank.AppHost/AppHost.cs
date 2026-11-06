var builder = DistributedApplication.CreateBuilder(args);

// Set by the user via `dotnet user-secrets set "Nim:ApiKey" ...`; never in appsettings.
var nimApiKey = builder.AddParameterFromConfiguration("nim-api-key", "Nim:ApiKey", secret: true);

var qdrant = builder.AddQdrant("qdrant")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

// Fake bank backend; its in-memory database is recreated and seeded on every start.
builder.AddProject<Projects.SelimBank_Api>("selimbank-api")
    .WithHttpHealthCheck("/health");

// The source PDFs live in the repository (docs/sources); the web app ingests them at startup.
string sourcesPath = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", "docs", "sources"));

builder.AddProject<Projects.Assistant_Web>("web")
    .WithReference(qdrant)
    .WaitFor(qdrant)
    .WithEnvironment("Nim__ApiKey", nimApiKey)
    .WithEnvironment("Rag__SourcesPath", sourcesPath)
    .WithExternalHttpEndpoints();

builder.Build().Run();
