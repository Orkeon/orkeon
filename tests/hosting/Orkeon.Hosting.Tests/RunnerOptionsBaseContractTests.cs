namespace Orkeon.Hosting.Tests;

/// <summary>
/// Contract tests for <see cref="RunnerOptionsBase"/> — the CLI option surface every
/// runner (and the `orkeon` tool's YAML path) inherits from <c>Orkeon.Hosting</c>.
/// </summary>
public class RunnerOptionsBaseContractTests
{
    private sealed class TestOptions : RunnerOptionsBase { }

    // --- ResolvedLlmLogPath ----------------------------------------------------

    [Fact]
    public void ResolvedLlmLogPath_null_when_logging_disabled()
    {
        var opts = new TestOptions();
        Assert.Null(opts.ResolvedLlmLogPath);
    }

    [Fact]
    public void ResolvedLlmLogPath_defaults_to_llm_logs_dir_when_flag_set()
    {
        var opts = new TestOptions { LlmLogEnabled = true };
        var resolved = opts.ResolvedLlmLogPath;
        Assert.NotNull(resolved);
        Assert.True(Path.IsPathRooted(resolved));
        Assert.Equal("llm-logs", Path.GetFileName(resolved));
    }

    [Fact]
    public void ResolvedLlmLogPath_explicit_path_implies_llm_log()
    {
        var opts = new TestOptions { LlmLogPath = "my-logs" };
        var resolved = opts.ResolvedLlmLogPath;
        Assert.NotNull(resolved);
        Assert.True(Path.IsPathRooted(resolved));
        Assert.Equal("my-logs", Path.GetFileName(resolved));
    }

    // --- ParsedVariables (canary cases — full matrix lives with the runners) ----

    [Fact]
    public void ParsedVariables_empty_without_var_args()
    {
        Assert.Empty(new TestOptions().ParseVariables());
    }

    [Fact]
    public void ParsedVariables_parses_key_value_pairs()
    {
        var opts = new TestOptions { Variables = ["topic=AI", "depth=3"] };
        var vars = opts.ParseVariables();
        Assert.Equal(2, vars.Count);
        Assert.Equal("AI", vars["topic"]);
        Assert.Equal("3", vars["depth"]);
    }

    [Fact]
    public void ParsedVariables_throws_on_malformed_entry()
    {
        var opts = new TestOptions { Variables = ["no-equals-sign"] };
        Assert.Throws<FormatException>(() => opts.ParseVariables());
    }
}
