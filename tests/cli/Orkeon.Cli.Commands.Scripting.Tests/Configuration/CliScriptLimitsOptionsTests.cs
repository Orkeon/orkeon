using Orkeon.Cli.Commands.Scripting.Configuration;

namespace Orkeon.Cli.Commands.Scripting.Tests.Configuration;

public sealed class CliScriptLimitsOptionsTests
{
    [Fact]
    public void Defaults_match_cli_profile_spec_8_1()
    {
        var sut = new CliScriptLimitsOptions();
        Assert.Equal(64L * 1024 * 1024, sut.MemoryLimitBytes);  // 64 MB
        Assert.Equal(100, sut.RecursionLimit);
        Assert.Equal(TimeSpan.FromMinutes(5), sut.ExecutionTimeout);
    }

    [Fact]
    public void ToScriptingLimits_projects_all_fields()
    {
        var sut = new CliScriptLimitsOptions
        {
            MemoryLimitBytes = 32 * 1024 * 1024,
            RecursionLimit = 50,
            ExecutionTimeout = TimeSpan.FromMinutes(2),
        };
        var projected = sut.ToScriptingLimits();
        Assert.Equal(sut.MemoryLimitBytes, projected.MemoryLimitBytes);
        Assert.Equal(sut.RecursionLimit, projected.RecursionLimit);
        Assert.Equal(sut.ExecutionTimeout, projected.ExecutionTimeout);
    }
}
