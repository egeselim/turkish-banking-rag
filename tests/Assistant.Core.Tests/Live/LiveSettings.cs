using Assistant.Core.Models;
using Microsoft.Extensions.Configuration;

namespace Assistant.Core.Tests.Live;

/// <summary>
/// Settings for tests that call the real NIM API. They read the same user-secrets as the AppHost
/// (or NVIDIA_API_KEY) and are skipped when no key is present, e.g. in CI without secrets.
/// </summary>
internal static class LiveSettings
{
    // UserSecretsId of src/SelimBank.AppHost.
    private const string AppHostUserSecretsId = "6d0b069d-b853-426f-9693-9b43173f83e5";

    // Model ids come from the Web app's appsettings.json, so live tests check exactly what the app runs.
    private static readonly Lazy<IConfiguration> LazyConfiguration = new(() =>
        new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepoRoot(), "src", "Assistant.Web", "appsettings.json"), optional: false)
            .AddUserSecrets(AppHostUserSecretsId)
            .AddEnvironmentVariables()
            .Build());

    public static IConfiguration Configuration => LazyConfiguration.Value;

    public static void RequireApiKey() => RequireOptions();

    public static NimOptions RequireOptions()
    {
        var options = new NimOptions();
        Configuration.GetSection(NimOptions.SectionName).Bind(options);
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            options.ApiKey = Configuration["NVIDIA_API_KEY"];
        }

        Assert.SkipWhen(string.IsNullOrWhiteSpace(options.ApiKey), "No NIM API key (Nim:ApiKey / NVIDIA_API_KEY); live test skipped.");
        return options;
    }

    public static HttpClient CreateHttpClient(NimOptions options) => new() { Timeout = options.RequestTimeout };

    /// <summary>Repo-local, git-ignored folder for experiment output.</summary>
    public static string ArtifactsDirectory(string name)
    {
        string path = Path.Combine(RepoRoot(), "artifacts", name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SelimBank.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root (SelimBank.slnx) not found.");
    }
}
