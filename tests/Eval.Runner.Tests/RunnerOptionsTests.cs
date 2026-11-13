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
        Assert.Equal(["nvidia/a", "nvidia/b"], RunnerOptions.Parse(["--rerank", "nvidia/a, nvidia/b"]).RerankModels);
    }

    [Fact]
    public void Answer_mode_takes_prices_and_a_limit()
    {
        var options = RunnerOptions.Parse(["--mode", "answer", "--limit", "3", "--price-in", "0.5", "--price-out", "1.5", "--judge", "small"]);

        Assert.Equal("answer", options.Mode);
        Assert.Equal("small", options.Judge);
        Assert.Equal("main", RunnerOptions.Parse(["--mode", "answer"]).Judge);
        Assert.Equal(3, options.Limit);
        Assert.Equal(0.5, options.PriceIn);
        Assert.Equal(1.5, options.PriceOut);
        Assert.Equal("baseline-answer", options.BaselineName);
    }

    [Theory]
    [InlineData("--subset", "all")]
    [InlineData("--mode", "other")]
    [InlineData("--mode", "answer", "--offline")]
    [InlineData("--mode", "answer", "--fail-under-hit5", "0.8")]
    [InlineData("--price-in", "1", "--price-out", "1")]
    [InlineData("--mode", "answer", "--price-in", "1")]
    [InlineData("--limit", "0")]
    [InlineData("--mode", "answer", "--judge", "big")]
    [InlineData("--mode", "answer", "--rerank", "nvidia/x")]
    [InlineData("--mode", "answer", "--fail-on-regression")]
    [InlineData("--fail-on-regression", "--no-compare")]
    [InlineData("--subset", "full", "--baseline", "--limit", "5")]
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
