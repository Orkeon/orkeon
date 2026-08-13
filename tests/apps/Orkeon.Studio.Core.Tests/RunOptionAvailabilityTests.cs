using Orkeon.Studio.Core.Launch;
using Orkeon.Studio.Core.Targets;

namespace Orkeon.Studio.Core.Tests;

/// <summary>
/// What a launcher form offers per target shape: the YAML options and the script options
/// never appear together, and everything else is shared.
/// </summary>
public sealed class RunOptionAvailabilityTests
{
    private static RunTarget Target(RunTargetKind kind) => new()
    {
        Kind = kind,
        SelectedPath = "/crews/target",
        RunPath = "/crews/target",
    };

    [Fact]
    public void A_yaml_target_offers_the_variables_but_not_the_inputs()
    {
        var options = RunOptionAvailability.For(RunTargetDialect.Yaml);

        Assert.Contains(RunOption.Variables, options);
        Assert.Contains(RunOption.InitialContext, options);
        Assert.DoesNotContain(RunOption.Inputs, options);
        Assert.DoesNotContain(RunOption.InputsFile, options);
    }

    [Fact]
    public void A_script_target_offers_the_inputs_but_not_the_variables()
    {
        var options = RunOptionAvailability.For(RunTargetDialect.Script);

        Assert.Contains(RunOption.Inputs, options);
        Assert.Contains(RunOption.InputsFile, options);
        Assert.DoesNotContain(RunOption.Variables, options);
        Assert.DoesNotContain(RunOption.InitialContext, options);
    }

    [Theory]
    [InlineData(RunOption.Settings)]
    [InlineData(RunOption.Mounts)]
    [InlineData(RunOption.AllowExternalMounts)]
    [InlineData(RunOption.Verbose)]
    [InlineData(RunOption.LlmLog)]
    [InlineData(RunOption.LlmLogPath)]
    [InlineData(RunOption.Validate)]
    public void The_host_bootstrap_options_are_shared_by_both_dialects(RunOption option)
    {
        Assert.True(RunOptionAvailability.IsAvailable(RunTargetDialect.Yaml, option));
        Assert.True(RunOptionAvailability.IsAvailable(RunTargetDialect.Script, option));
    }

    [Theory]
    [InlineData(RunTargetKind.YamlFile, RunTargetDialect.Yaml)]
    [InlineData(RunTargetKind.MultiFileCrewDirectory, RunTargetDialect.Yaml)]
    [InlineData(RunTargetKind.ScriptFile, RunTargetDialect.Script)]
    [InlineData(RunTargetKind.ScriptDirectory, RunTargetDialect.Script)]
    public void Every_target_kind_maps_to_its_dialect(RunTargetKind kind, RunTargetDialect dialect)
    {
        var target = Target(kind);

        Assert.Equal(dialect, target.Dialect);
        Assert.Equal(RunOptionAvailability.For(dialect), RunOptionAvailability.For(target));
    }

    [Fact]
    public void Every_option_is_named_as_the_cli_spells_it()
    {
        Assert.Equal("--settings", RunOptionAvailability.ToCommandLineName(RunOption.Settings));
        Assert.Equal("-V", RunOptionAvailability.ToCommandLineName(RunOption.Variables));
        Assert.Equal("--initial-context", RunOptionAvailability.ToCommandLineName(RunOption.InitialContext));
        Assert.Equal("--inputs", RunOptionAvailability.ToCommandLineName(RunOption.Inputs));
        Assert.Equal("--inputs-file", RunOptionAvailability.ToCommandLineName(RunOption.InputsFile));
        Assert.Equal("--mount", RunOptionAvailability.ToCommandLineName(RunOption.Mounts));
        Assert.Equal("--allow-external-mounts", RunOptionAvailability.ToCommandLineName(RunOption.AllowExternalMounts));
        Assert.Equal("--verbose", RunOptionAvailability.ToCommandLineName(RunOption.Verbose));
        Assert.Equal("--llm-log", RunOptionAvailability.ToCommandLineName(RunOption.LlmLog));
        Assert.Equal("--llm-log-path", RunOptionAvailability.ToCommandLineName(RunOption.LlmLogPath));
        Assert.Equal("--validate", RunOptionAvailability.ToCommandLineName(RunOption.Validate));
    }
}
