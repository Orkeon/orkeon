namespace Orkeon.ConsoleApp.Tests;

/// <summary>
/// The REPL's own flags. <c>--allow-external-mounts</c> is the runners' opt-in (GAP-49, decision 3):
/// a bare flag, or <c>=true</c> / <c>=false</c>, that never takes the next argument as its value.
/// </summary>
public sealed class ScriptedCommandsCliOptionsTests
{
    [Theory]
    [InlineData(new[] { "--allow-external-mounts" }, true)]
    [InlineData(new[] { "--allow-external-mounts=true" }, true)]
    [InlineData(new[] { "--allow-external-mounts=1" }, true)]
    [InlineData(new[] { "--allow-external-mounts=false" }, false)]
    [InlineData(new[] { "--runner=scripted-commands" }, false)]
    public void Allow_external_mounts_reads_the_flag(string[] args, bool expected)
        => Assert.Equal(expected, ScriptedCommandsCliOptions.Parse(args).AllowExternalMounts);

    [Fact]
    public void Allow_external_mounts_does_not_take_the_next_argument()
    {
        var options = ScriptedCommandsCliOptions.Parse(["--allow-external-mounts", "--mount", "/a:/workspace:rw"]);

        Assert.True(options.AllowExternalMounts);
        Assert.Equal("/a:/workspace:rw", Assert.Single(options.Mounts));
    }

    [Theory]
    [InlineData(new[] { "--llm-log" }, true, null)]
    [InlineData(new[] { "--llm-log-path", "logs" }, false, "logs")]
    [InlineData(new[] { "--llm-log-path=logs" }, false, "logs")]
    [InlineData(new[] { "--llm-log-path", "a", "--llm-log-path", "b" }, false, "b")]
    [InlineData(new[] { "--runner=scripted-commands" }, false, null)]
    public void Llm_log_reads_the_flag_and_the_folder(string[] args, bool enabled, string? path)
    {
        var options = ScriptedCommandsCliOptions.Parse(args);

        Assert.Equal(enabled, options.LlmLogEnabled);
        Assert.Equal(path, options.LlmLogPath);
    }

    [Theory]
    [InlineData(new[] { "--llm-log" }, "llm-logs")]
    [InlineData(new[] { "--llm-log-path", "my-logs" }, "my-logs")]
    public void Llm_log_resolves_the_folder_as_orkeon_run_does(string[] args, string folder)
    {
        var resolved = ScriptedCommandsCliOptions.Parse(args).ResolvedLlmLogPath;

        Assert.NotNull(resolved);
        Assert.True(Path.IsPathRooted(resolved));
        Assert.Equal(folder, Path.GetFileName(resolved));
    }

    [Fact]
    public void Llm_log_is_off_without_either_flag()
        => Assert.Null(ScriptedCommandsCliOptions.Parse(["--mount", "/a:/workspace:rw"]).ResolvedLlmLogPath);
}
