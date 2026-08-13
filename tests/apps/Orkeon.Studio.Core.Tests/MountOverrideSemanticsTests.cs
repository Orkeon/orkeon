using Orkeon.Studio.Core.Launch;
using Orkeon.Studio.Core.Targets;

namespace Orkeon.Studio.Core.Tests;

/// <summary>
/// What a launch really does to the mount list. The runner writes ONE array — its own
/// auto-injected mounts first, then the <c>--mount</c> arguments — as
/// <c>Orkeon:FileSystem:Mounts:{i}</c>. So index 0 is never the user's, the first
/// <c>--mount</c> lands at index 1 (2 with <c>--llm-log</c>), and nothing is ever merged.
/// </summary>
public sealed class MountOverrideSemanticsTests
{
    private static readonly string[] NoMounts = [];

    private static readonly string[] YamlArguments = ["/srv/crew.yaml"];

    /// <summary>A YAML file target, whose auto-injected mount is its own directory, 1:1 read-only.</summary>
    private static RunTarget YamlTarget() => new()
    {
        Kind = RunTargetKind.YamlFile,
        SelectedPath = YamlArguments[0],
        RunPath = YamlArguments[0],
    };

    private static RunTarget ScriptTarget() => new()
    {
        Kind = RunTargetKind.ScriptFile,
        SelectedPath = "/srv/scripts/crew.ork.ts",
        RunPath = "/srv/scripts/crew.ork.ts",
    };

    private static MountAutoInjection Injection(RunLaunchOptions? options = null) =>
        MountAutoInjection.For(YamlTarget(), options);

    [Fact]
    public void The_runner_always_injects_the_crew_directory_before_the_user_mounts()
    {
        var injection = Injection();

        var mount = Assert.Single(injection.Mounts);
        Assert.Equal(1, injection.Count);
        Assert.EndsWith(":ro", mount, StringComparison.Ordinal);
        Assert.Contains(Path.GetFullPath("/srv"), mount, StringComparison.Ordinal);
    }

    [Fact]
    public void A_script_target_is_mounted_under_slash_script_instead()
    {
        var mount = Assert.Single(MountAutoInjection.For(ScriptTarget()).Mounts);

        Assert.EndsWith(":/script:ro", mount, StringComparison.Ordinal);
    }

    [Fact]
    public void Llm_logging_adds_a_second_injected_mount()
    {
        var injection = Injection(new RunLaunchOptions { LlmLogEnabled = true, LlmLogPath = "/var/log/orkeon" });

        Assert.Equal(2, injection.Count);
        Assert.Equal($"{Path.GetFullPath("/var/log/orkeon")}:{Path.GetFullPath("/var/log/orkeon")}:rw", injection.Mounts[1]);
    }

    [Fact]
    public void A_bare_llm_log_path_implies_logging_just_as_the_cli_does()
    {
        Assert.Equal(2, Injection(new RunLaunchOptions { LlmLogPath = "/var/log/orkeon" }).Count);
        Assert.Equal(1, Injection(new RunLaunchOptions()).Count);
    }

    [Fact]
    public void The_settings_entry_at_index_zero_is_always_masked_by_the_injected_mount()
    {
        var injection = Injection();

        var effective = MountOverrideSemantics.ComputeEffectiveMounts(
            NoMounts,
            ["/srv/configured:/workspace:ro"],
            injection);

        var entry = Assert.Single(effective);
        Assert.Equal(0, entry.Index);
        Assert.Equal(MountOrigin.AutoInjected, entry.Origin);
        Assert.Equal(injection.Mounts[0], entry.Value);
        Assert.Equal("/srv/configured:/workspace:ro", entry.ReplacedSettingsMount);
        Assert.True(entry.OverridesSettings);
    }

    [Fact]
    public void The_first_command_line_mount_lands_at_index_one_not_index_zero()
    {
        var effective = MountOverrideSemantics.ComputeEffectiveMounts(
            ["/srv/run:/workspace:rw"],
            ["/srv/configured:/workspace:ro", "/srv/also:/data:ro"],
            Injection());

        Assert.Equal(MountOrigin.AutoInjected, effective[0].Origin);

        Assert.Equal(1, effective[1].Index);
        Assert.Equal("/srv/run:/workspace:rw", effective[1].Value);
        Assert.Equal(MountOrigin.CommandLine, effective[1].Origin);
        Assert.Equal("/srv/also:/data:ro", effective[1].ReplacedSettingsMount);
        Assert.Equal("Orkeon:FileSystem:Mounts:1", effective[1].ConfigurationKey);
    }

    [Fact]
    public void With_llm_logging_the_first_command_line_mount_shifts_to_index_two()
    {
        var effective = MountOverrideSemantics.ComputeEffectiveMounts(
            ["/srv/run:/workspace:rw"],
            NoMounts,
            Injection(new RunLaunchOptions { LlmLogEnabled = true }));

        Assert.Equal(3, effective.Count);
        Assert.Equal(MountOrigin.AutoInjected, effective[0].Origin);
        Assert.Equal(MountOrigin.AutoInjected, effective[1].Origin);
        Assert.Equal(MountOrigin.CommandLine, effective[2].Origin);
        Assert.Equal("Orkeon:FileSystem:Mounts:2", effective[2].ConfigurationKey);
    }

    [Fact]
    public void Settings_entries_past_the_last_written_index_stay_in_force()
    {
        var effective = MountOverrideSemantics.ComputeEffectiveMounts(
            ["/srv/run:/workspace:rw"],
            ["/srv/a:/a:ro", "/srv/b:/b:ro", "/srv/out:/output:rw"],
            Injection());

        Assert.Equal(3, effective.Count);
        Assert.Equal(MountOrigin.Settings, effective[2].Origin);
        Assert.Equal("/srv/out:/output:rw", effective[2].Value);
        Assert.Null(effective[2].ReplacedSettingsMount);
        Assert.False(effective[2].OverridesSettings);
    }

    [Fact]
    public void A_command_line_mount_past_the_settings_list_overrides_nothing()
    {
        var effective = MountOverrideSemantics.ComputeEffectiveMounts(
            ["/srv/a:/workspace:ro", "/srv/b:/output:rw"],
            ["/srv/configured:/workspace:ro"],
            Injection());

        Assert.Equal(3, effective.Count);
        Assert.True(effective[0].OverridesSettings);
        Assert.Equal(MountOrigin.CommandLine, effective[1].Origin);
        Assert.Null(effective[1].ReplacedSettingsMount);
        Assert.False(effective[1].OverridesSettings);
    }

    [Fact]
    public void The_configuration_key_matches_the_one_the_runner_writes()
    {
        Assert.Equal("Orkeon:FileSystem:Mounts:0", MountOverrideSemantics.ConfigurationKey(0));
        Assert.Equal("Orkeon:FileSystem:Mounts:12", MountOverrideSemantics.ConfigurationKey(12));
        Assert.Throws<ArgumentOutOfRangeException>(() => MountOverrideSemantics.ConfigurationKey(-1));
    }

    [Fact]
    public void The_explanations_state_the_offset_the_override_and_the_whitelisting()
    {
        Assert.Contains("replaces", MountOverrideSemantics.Explanation, StringComparison.Ordinal);
        Assert.Contains("index 1", MountOverrideSemantics.Explanation, StringComparison.Ordinal);
        Assert.Contains(
            MountOverrideSemantics.ConfigurationSection,
            MountOverrideSemantics.Explanation,
            StringComparison.Ordinal);
        Assert.Contains(
            MountOverrideSemantics.ExternalMountsConfigurationSection,
            MountOverrideSemantics.ExternalMountsExplanation,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_per_launch_explanation_names_the_index_this_launch_will_use()
    {
        var text = MountOverrideSemantics.Explain(
            Injection(new RunLaunchOptions { LlmLogEnabled = true }));

        Assert.Contains("Orkeon:FileSystem:Mounts:2", text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_mount_argument_is_reported_against_the_key_it_will_really_occupy()
    {
        var messages = RunArgumentsBuilder.Validate(
            YamlTarget(),
            new RunLaunchOptions { Mounts = ["   "] });

        var message = Assert.Single(messages);
        Assert.Equal(LaunchCodes.EmptyMount, message.Code);
        Assert.Equal("Orkeon:FileSystem:Mounts:1", message.Path);
    }
}
