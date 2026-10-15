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
}
