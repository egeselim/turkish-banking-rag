using Assistant.Core.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Assistant.Core.Tests.Telemetry;

public sealed class ModelTelemetryTests
{
    private const string CaptureContentVariable = "OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT";

    // The variable turns content capture on by default; the explicit setting in AddNimModels must win.
    [Fact]
    public void Model_telemetry_never_records_content_even_if_the_environment_asks_for_it()
    {
        Environment.SetEnvironmentVariable(CaptureContentVariable, "true");
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Nim:ApiKey"] = "test-key-not-real",
                    ["Nim:ChatModel"] = "test/chat",
                    ["Nim:SmallChatModel"] = "test/small",
                    ["Nim:EmbeddingModel"] = "test/embed",
                })
                .Build();
            using var services = new ServiceCollection().AddNimModels(configuration).BuildServiceProvider();

            var chat = services.GetRequiredService<IChatClient>().GetService<OpenTelemetryChatClient>();
            var small = services.GetRequiredKeyedService<IChatClient>(ModelServiceCollectionExtensions.SmallChatClientKey).GetService<OpenTelemetryChatClient>();
            var embeddings = services.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>()
                .GetService<OpenTelemetryEmbeddingGenerator<string, Embedding<float>>>();

            Assert.False(Assert.IsType<OpenTelemetryChatClient>(chat).EnableSensitiveData);
            Assert.False(Assert.IsType<OpenTelemetryChatClient>(small).EnableSensitiveData);
            Assert.False(Assert.IsType<OpenTelemetryEmbeddingGenerator<string, Embedding<float>>>(embeddings).EnableSensitiveData);
        }
        finally
        {
            Environment.SetEnvironmentVariable(CaptureContentVariable, null);
        }
    }
}
