using System.Reflection;

namespace Assistant.Core.Tests;

// Project rule: provider SDK types must not leak outside Assistant.Core.
public class ArchitectureTests
{
    private static readonly string[] ProviderSdkPrefixes = ["OpenAI", "Microsoft.Extensions.AI.OpenAI"];

    [Theory]
    [InlineData("Assistant.Privacy")]
    [InlineData("Assistant.Rag")]
    public void Library_does_not_reference_provider_sdks(string assemblyName)
    {
        var references = Assembly.Load(assemblyName).GetReferencedAssemblies().Select(a => a.Name!);

        var leaked = references.Where(r => ProviderSdkPrefixes.Any(p => r.StartsWith(p, StringComparison.Ordinal)));

        Assert.Empty(leaked);
    }

    [Fact]
    public void Privacy_has_no_project_dependencies()
    {
        var references = Assembly.Load("Assistant.Privacy").GetReferencedAssemblies().Select(a => a.Name!);

        Assert.DoesNotContain(references, r => r.StartsWith("Assistant.", StringComparison.Ordinal));
    }
}
