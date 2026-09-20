using Orkeon.Constants.FileSystem;
using Orkeon.Studio.Core.Launch;
using Orkeon.Studio.Core.Targets;

namespace Orkeon.Studio.Core.Tests;

/// <summary>
/// What a launch really does to the mount list (STUDIO-15 D-04). The runner places its own
/// auto-injected mount, then every <c>--mount</c> argument, in <c>Orkeon:FileSystem:Mounts</c>
/// <em>by virtual root</em>: a launch mount on a root the settings declare is written at that
/// entry's index and replaces it; a launch mount on a new root is appended after every
/// declared entry. Since ADR-008 the exchange log is an internal mount on its own key, so
/// <c>--llm-log</c> shifts nothing.
/// </summary>
public sealed class MountOverrideSemanticsTests
{
    private static readonly string[] NoMounts = [];

    private static readonly string[] YamlArguments = ["/srv/crew.yaml"];

    /// <summary>A YAML file target, whose auto-injected mount is its own directory as <c>/crew</c>, read-only.</summary>
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

    /// <summary>
    /// Since ADR-008 the exchange log is an INTERNAL mount under its own configuration key:
    /// it is named, hidden from agents, and — the part the UI depends on — it no longer
    /// shifts the indices of anything the user wrote.
    /// </summary>
    [Fact]
    public void Llm_logging_adds_an_internal_mount_that_shifts_nothing()
    {
        var injection = Injection(new RunLaunchOptions { LlmLogEnabled = true, LlmLogPath = "/var/log/orkeon" });

        Assert.Equal(1, injection.Count);
        var internalMount = Assert.Single(injection.InternalMounts);
        Assert.Equal(
            $"{Path.GetFullPath("/var/log/orkeon")}:{RunnerVirtualRoots.LlmLogs}:rw",
            internalMount);
    }

    [Fact]
    public void A_bare_llm_log_path_implies_logging_just_as_the_cli_does()
    {
        Assert.Single(Injection(new RunLaunchOptions { LlmLogPath = "/var/log/orkeon" }).InternalMounts);
        Assert.Empty(Injection(new RunLaunchOptions()).InternalMounts);
        Assert.Equal(1, Injection(new RunLaunchOptions { LlmLogPath = "/var/log/orkeon" }).Count);
    }

    [Fact]
    public void A_command_line_mount_replaces_the_settings_entry_with_the_same_root()
    {
        var effective = MountOverrideSemantics.ComputeEffectiveMounts(
            ["/srv/run:/workspace:rw"],
            ["/srv/configured:/workspace:ro", "/srv/also:/data:ro"],
            Injection());

        Assert.Equal(3, effective.Count);

        // Written at the settings entry's own index — index 0 here — whatever its position
        // on the command line.
        Assert.Equal(0, effective[0].Index);
        Assert.Equal("/srv/run:/workspace:rw", effective[0].Value);
        Assert.Equal(MountOrigin.CommandLine, effective[0].Origin);
        Assert.Equal("/srv/configured:/workspace:ro", effective[0].ReplacedSettingsMount);
        Assert.True(effective[0].OverridesSettings);

        // The other settings entry is untouched, in place.
        Assert.Equal(new EffectiveMount(1, "/srv/also:/data:ro", MountOrigin.Settings), effective[1]);
    }

    /// <summary>VFS-90: among several entries of one root, the one a <c>--mount-id</c> names is kept.</summary>
    [Fact]
    public void A_mount_id_keeps_one_entry_of_a_shared_root_and_the_others_are_not_mounted()
    {
        var a = Orkeon.Domain.Common.MountId.Create();
        var b = Orkeon.Domain.Common.MountId.Create();

        var effective = MountOverrideSemantics.ComputeEffectiveMounts(
            [],
            [$"{a}|/srv/a:/output:rw", $"{b}|/srv/b:/output:rw", "/srv/also:/data:ro"],
            Injection(),
            mountIds: [b.ToString()]);

        Assert.Equal(EffectiveMountSelection.NotSelected, effective[0].Selection);
        Assert.Equal(EffectiveMountSelection.SelectedById, effective[1].Selection);
        Assert.Equal(EffectiveMountSelection.InForce, effective[2].Selection);
        Assert.Equal(2, effective[0].SharedRootCount);
        Assert.Equal(2, effective[1].SharedRootCount);
        Assert.Equal(1, effective[2].SharedRootCount);
        Assert.False(effective[0].IsMounted);
        Assert.True(effective[1].IsMounted);
    }

    [Fact]
    public void A_shared_root_nothing_selects_reads_as_a_conflict()
    {
        var effective = MountOverrideSemantics.ComputeEffectiveMounts(
            [],
            [$"{Orkeon.Domain.Common.MountId.Create()}|/srv/a:/output:rw", $"{Orkeon.Domain.Common.MountId.Create()}|/srv/b:/output:rw"],
            Injection());

        Assert.All(effective.Where(m => m.Origin == MountOrigin.Settings), m => Assert.Equal(EffectiveMountSelection.Conflict, m.Selection));
    }

    [Fact]
    public void A_command_line_mount_on_a_shared_root_replaces_the_first_entry_and_withdraws_the_rest()
    {
        var effective = MountOverrideSemantics.ComputeEffectiveMounts(
            ["/srv/run:/output:rw"],
            [$"{Orkeon.Domain.Common.MountId.Create()}|/srv/a:/output:rw", $"{Orkeon.Domain.Common.MountId.Create()}|/srv/b:/output:rw"],
            Injection(),
            mountIds: [Orkeon.Domain.Common.MountId.Create().ToString()]);

        Assert.Equal(MountOrigin.CommandLine, effective[0].Origin);
        Assert.Equal(EffectiveMountSelection.InForce, effective[0].Selection);
        Assert.Equal(EffectiveMountSelection.NotSelected, effective[1].Selection);
        Assert.Equal(2, effective[1].SharedRootCount);
    }

    [Fact]
    public void A_new_root_lands_after_every_settings_entry()
    {
        var injection = Injection();

        var effective = MountOverrideSemantics.ComputeEffectiveMounts(
            ["/srv/run:/extra:rw"],
            ["/srv/a:/a:ro", "/srv/b:/b:ro"],
            injection);

        Assert.Equal(4, effective.Count);
        Assert.Equal(MountOrigin.Settings, effective[0].Origin);
        Assert.Equal(MountOrigin.Settings, effective[1].Origin);

        // The runner's own mount first, then the --mount — both appended, neither replacing.
        Assert.Equal(new EffectiveMount(2, injection.Mounts[0], MountOrigin.AutoInjected), effective[2]);
        Assert.Equal(new EffectiveMount(3, "/srv/run:/extra:rw", MountOrigin.CommandLine), effective[3]);
        Assert.False(effective[2].OverridesSettings);
        Assert.False(effective[3].OverridesSettings);
    }

    [Fact]
    public void The_configuration_key_follows_the_final_array()
    {
        var effective = MountOverrideSemantics.ComputeEffectiveMounts(
            ["/srv/run:/data:rw", "/srv/out:/output:rw"],
            ["/srv/a:/a:ro", "/srv/configured:/data:ro"],
            Injection());

        Assert.Equal(
            ["Orkeon:FileSystem:Mounts:0", "Orkeon:FileSystem:Mounts:1", "Orkeon:FileSystem:Mounts:2", "Orkeon:FileSystem:Mounts:3"],
            effective.Select(m => m.ConfigurationKey));
        Assert.Equal("/srv/run:/data:rw", effective[1].Value);
        Assert.Equal(MountOrigin.AutoInjected, effective[2].Origin);
        Assert.Equal("/srv/out:/output:rw", effective[3].Value);
    }

    [Fact]
    public void With_no_settings_entry_the_crew_mount_opens_the_array()
    {
        var injection = Injection(new RunLaunchOptions { LlmLogEnabled = true });

        var effective = MountOverrideSemantics.ComputeEffectiveMounts(
            ["/srv/run:/workspace:rw"],
            NoMounts,
            injection);

        Assert.Equal(2, effective.Count);
        Assert.Equal(new EffectiveMount(0, injection.Mounts[0], MountOrigin.AutoInjected), effective[0]);
        Assert.Equal(new EffectiveMount(1, "/srv/run:/workspace:rw", MountOrigin.CommandLine), effective[1]);
    }

    /// <summary>
    /// The runner drops a trailing slash when it compares roots, so the table must too — or it
    /// would predict two mounts where the runner writes one.
    /// </summary>
    [Fact]
    public void A_trailing_slash_names_the_same_root()
    {
        var effective = MountOverrideSemantics.ComputeEffectiveMounts(
            ["/srv/run:/data:rw"],
            ["/srv/configured:/data/:ro"],
            Injection());

        Assert.Equal(2, effective.Count);
        Assert.Equal("/srv/configured:/data/:ro", effective[0].ReplacedSettingsMount);
        Assert.Equal(MountOrigin.AutoInjected, effective[1].Origin);
    }

    /// <summary>
    /// The runner refuses two <c>--mount</c> on one root before any host boots (D-02), so
    /// the table never has to be right about that launch — but it must not crash on it.
    /// Two launch mounts on one declared root overwrite the same key, last one wins.
    /// </summary>
    [Fact]
    public void Two_launch_mounts_on_one_declared_root_take_the_same_key_last_one_wins()
    {
        var effective = MountOverrideSemantics.ComputeEffectiveMounts(
            ["/srv/first:/data:ro", "/srv/second:/data:rw"],
            ["/srv/configured:/data:ro"],
            Injection());

        Assert.Equal(2, effective.Count);
        Assert.Equal("/srv/second:/data:rw", effective[0].Value);
        Assert.Equal("/srv/configured:/data:ro", effective[0].ReplacedSettingsMount);
    }

    [Fact]
    public void An_unreadable_launch_mount_is_filed_as_appended()
    {
        var effective = MountOverrideSemantics.ComputeEffectiveMounts(
            ["not-a-mount"],
            ["/srv/a:/a:ro"],
            Injection());

        Assert.Equal(3, effective.Count);
        Assert.Equal(new EffectiveMount(2, "not-a-mount", MountOrigin.CommandLine), effective[2]);
    }

    [Fact]
    public void The_configuration_key_matches_the_one_the_runner_writes()
    {
        Assert.Equal("Orkeon:FileSystem:Mounts:0", MountOverrideSemantics.ConfigurationKey(0));
        Assert.Equal("Orkeon:FileSystem:Mounts:12", MountOverrideSemantics.ConfigurationKey(12));
        Assert.Throws<ArgumentOutOfRangeException>(() => MountOverrideSemantics.ConfigurationKey(-1));
    }

    [Fact]
    public void The_explanations_state_the_by_root_rule_and_the_whitelisting()
    {
        Assert.Contains("replaces every settings entry of that root", MountOverrideSemantics.Explanation, StringComparison.Ordinal);
        Assert.Contains("--mount-id", MountOverrideSemantics.Explanation, StringComparison.Ordinal);
        Assert.Contains("appended", MountOverrideSemantics.Explanation, StringComparison.Ordinal);
        Assert.DoesNotContain("index 1", MountOverrideSemantics.Explanation, StringComparison.Ordinal);
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
    public void The_per_launch_explanation_names_the_mounts_this_launch_inserts()
    {
        var injection = Injection(new RunLaunchOptions { LlmLogEnabled = true });

        var text = MountOverrideSemantics.Explain(injection);

        Assert.Contains(injection.Mounts[0], text, StringComparison.Ordinal);
        Assert.Contains("after the settings entries", text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_mount_argument_is_reported_against_the_mount_option()
    {
        var messages = RunArgumentsBuilder.Validate(
            YamlTarget(),
            new RunLaunchOptions { Mounts = ["   "] });

        var message = Assert.Single(messages);
        Assert.Equal(LaunchCodes.EmptyMount, message.Code);
        // Not a configuration key: which one the entry would occupy depends on the settings
        // entries the runner will find, which the arguments alone do not know.
        Assert.Equal("--mount", message.Path);
    }
}
