using Orkeon.Studio.Config.Cli;

namespace Orkeon.Studio.Config.Tests.Cli;

public class StudioVersionTests
{
    [Fact]
    public void Version_comes_from_the_assembly_and_is_not_a_placeholder()
    {
        Assert.False(string.IsNullOrWhiteSpace(StudioVersion.Value));
        Assert.NotEqual("0.0.0", StudioVersion.Value);
    }

    [Fact]
    public void Version_carries_no_sourcelink_commit_suffix()
    {
        // `--version` output is compared by the onboarding smokes; "+<sha>" would break them.
        Assert.DoesNotContain('+', StudioVersion.Value);
    }

    [Fact]
    public void Version_line_is_a_single_line_naming_the_launcher()
    {
        Assert.StartsWith("orkeon-studio-config ", StudioVersion.Line);
        Assert.DoesNotContain('\n', StudioVersion.Line);
        Assert.EndsWith(StudioVersion.Value, StudioVersion.Line);
    }
}
