using Microsoft.Extensions.Configuration;

namespace Eval.Runner;

internal static class RepoPaths
{
    public const string QdrantImage = "qdrant/qdrant:v1.18.0";

    // UserSecretsId of src/SelimBank.AppHost: the runner reads the same NIM key as the app and the live tests.
    private const string AppHostUserSecretsId = "6d0b069d-b853-426f-9693-9b43173f83e5";

    public static string Root()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SelimBank.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repo kökü (SelimBank.slnx) bulunamadı; komutu repo içinden çalıştırın.");
    }

    /// <summary>
    /// Model ids from the Web app's appsettings.json (what the app runs), the key from user-secrets or
    /// NVIDIA_API_KEY, and the embedding cache switched on. In offline mode the key is not read at all.
    /// </summary>
    public static IConfiguration Configuration(string root, bool offline)
    {
        var builder = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(root, "src", "Assistant.Web", "appsettings.json"), optional: false);
        if (!offline)
        {
            builder.AddUserSecrets(AppHostUserSecretsId).AddEnvironmentVariables();
        }

        return builder
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Nim:EmbeddingCachePath"] = Path.Combine(root, "eval", "cache", "embeddings"),
                ["Nim:EmbeddingCacheOffline"] = offline ? "true" : "false",
            })
            .Build();
    }
}
