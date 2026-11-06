namespace SelimBank.Api.Tests;

// Plan dependency graph: SelimBank.Api → ServiceDefaults only. Core calls it over HTTP; it knows no Assistant.* code.
public class ArchitectureTests
{
    [Fact]
    public void Api_does_not_reference_assistant_assemblies()
    {
        var references = typeof(Program).Assembly.GetReferencedAssemblies().Select(a => a.Name!);

        Assert.DoesNotContain(references, r => r.StartsWith("Assistant.", StringComparison.Ordinal));
    }
}
