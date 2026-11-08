var builder = DistributedApplication.CreateBuilder(args);

// Set by the user via `dotnet user-secrets set "Nim:ApiKey" ...`; never in appsettings.
var nimApiKey = builder.AddParameterFromConfiguration("nim-api-key", "Nim:ApiKey", secret: true);

var qdrant = builder.AddQdrant("qdrant")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

// Fake bank backend; its in-memory database is recreated and seeded on every start.
var selimBankApi = builder.AddProject<Projects.SelimBank_Api>("selimbank-api")
    .WithHttpHealthCheck("/health");

// The source PDFs live in the repository (docs/sources); the web app ingests them at startup.
string sourcesPath = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", "docs", "sources"));

var web = builder.AddProject<Projects.Assistant_Web>("web")
    .WithReference(qdrant)
    .WaitFor(qdrant)
    .WithReference(selimBankApi)
    .WaitFor(selimBankApi)
    .WithEnvironment("Nim__ApiKey", nimApiKey)
    .WithEnvironment("Rag__SourcesPath", sourcesPath)
    .WithExternalHttpEndpoints();

// Optional (see README): without it the web app uses a random key and logs a warning.
if (!string.IsNullOrWhiteSpace(builder.Configuration["Audit:PseudonymKey"]))
{
    web.WithEnvironment(
        "Audit__PseudonymKey",
        builder.AddParameterFromConfiguration("audit-pseudonym-key", "Audit:PseudonymKey", secret: true));
}

builder.Build().Run();
