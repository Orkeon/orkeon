using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Scripting.Cli.Commands;
using Orkeon.Scripting.Configuration;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// Guards the effective-limits resolution of the <c>run</c> verb: the
/// <c>Orkeon:Scripting:Limits</c> appsettings section is the documented opt-in
/// for trusted long-running scripts (notably <c>ExecutionTimeout</c>, whose
/// wall-clock keeps ticking across awaited tool calls), and the
/// <c>--memory-limit-mb</c> CLI flag must MERGE into those bound options — the
/// historical bug replaced them wholesale, silently resetting
/// <c>ExecutionTimeout</c> back to the strict 30s default.
/// </summary>
public sealed class RunCommandLimitsTests
{
    private static IConfiguration ConfigWith(params (string Key, string Value)[] pairs)
    {
        var dict = pairs.ToDictionary(p => p.Key, p => (string?)p.Value);
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [Fact]
    public void No_section_and_no_flag_yields_strict_defaults()
    {
        var limits = RunCommand.ResolveEffectiveLimits(
            new RunCommandOptions(), ConfigWith(), NullLogger.Instance);

        Assert.Equal(new ScriptingLimitsOptions().ExecutionTimeout, limits.ExecutionTimeout);
        Assert.Equal(new ScriptingLimitsOptions().MemoryLimitBytes, limits.MemoryLimitBytes);
    }

    [Fact]
    public void Appsettings_section_raises_execution_timeout()
    {
        var config = ConfigWith(("Orkeon:Scripting:Limits:ExecutionTimeout", "00:45:00"));

        var limits = RunCommand.ResolveEffectiveLimits(
            new RunCommandOptions(), config, NullLogger.Instance);

        Assert.Equal(TimeSpan.FromMinutes(45), limits.ExecutionTimeout);
    }

    [Fact]
    public void Memory_flag_merges_without_resetting_bound_execution_timeout()
    {
        var config = ConfigWith(("Orkeon:Scripting:Limits:ExecutionTimeout", "00:45:00"));
        var options = new RunCommandOptions { MemoryLimitMb = 4096 };

        var limits = RunCommand.ResolveEffectiveLimits(options, config, NullLogger.Instance);

        Assert.Equal(4096L * 1024 * 1024, limits.MemoryLimitBytes);
        Assert.Equal(TimeSpan.FromMinutes(45), limits.ExecutionTimeout);
    }

    [Fact]
    public void Memory_flag_zero_disables_the_cap()
    {
        var limits = RunCommand.ResolveEffectiveLimits(
            new RunCommandOptions { MemoryLimitMb = 0 }, ConfigWith(), NullLogger.Instance);

        Assert.Equal(long.MaxValue, limits.MemoryLimitBytes);
    }
}
