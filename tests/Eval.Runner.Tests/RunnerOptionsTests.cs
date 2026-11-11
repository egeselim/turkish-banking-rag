namespace Eval.Runner.Tests;

public sealed class RunnerOptionsTests
{
    [Fact]
    public void Defaults_are_smoke_retrieval_online_with_baseline_compare()
    {
        var options = RunnerOptions.Parse([]);

        Assert.Equal("smoke", options.Subset);
        Assert.Equal("retrieval", options.Mode);
        Assert.False(options.Offline);
        Assert.Null(options.Qdrant);
        Assert.False(options.NoCompare);
    }

    [Fact]
    public void All_options_parse()
    {
        var options = RunnerOptions.Parse(
            ["--subset", "full", "--mode", "retrieval", "--offline", "--qdrant", "http://localhost:6334", "--out", "x", "--baseline", "--fail-under-hit5", "0.8"]);

        Assert.Equal("full", options.Subset);
        Assert.True(options.Offline);
        Assert.Equal(new Uri("http://localhost:6334"), options.Qdrant);
        Assert.Equal("x", options.OutDir);
        Assert.True(options.WriteBaseline);
        Assert.Equal(0.8, options.FailUnderHit5);
    }

    [Theory]
    [InlineData("--subset", "all")]
    [InlineData("--mode", "answer")]
    [InlineData("--mode", "other")]
    [InlineData("--baseline")]
    [InlineData("--qdrant", "localhost:6334")]
    [InlineData("--fail-under-hit5", "80")]
    [InlineData("--subset")]
    [InlineData("--unknown")]
    [InlineData("--compare", "a.json", "--no-compare")]
    public void Invalid_command_lines_are_rejected(params string[] args)
    {
        Assert.Throws<ArgumentException>(() => RunnerOptions.Parse(args));
    }
}
