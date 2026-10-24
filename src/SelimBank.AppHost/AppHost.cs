var builder = DistributedApplication.CreateBuilder(args);

// Set by the user via `dotnet user-secrets set "Nim:ApiKey" ...`; never in appsettings.
var nimApiKey = builder.AddParameterFromConfiguration("nim-api-key", "Nim:ApiKey", secret: true);

var qdrant = builder.AddQdrant("qdrant")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

builder.AddProject<Projects.Assistant_Web>("web")
    .WithReference(qdrant)
    .WaitFor(qdrant)
    .WithEnvironment("Nim__ApiKey", nimApiKey)
    .WithExternalHttpEndpoints();

builder.Build().Run();
