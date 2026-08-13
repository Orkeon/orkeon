using Orkeon.Studio.Core.Launch;

namespace Orkeon.Studio.Core.Tests;

/// <summary>
/// What a launch really does to the mount list: <c>--mount</c> arguments are injected as
/// <c>Orkeon:FileSystem:Mounts:{i}</c>, so they replace the appsettings entry of the same
/// index and leave the later ones alone. No merge, and the UI must be able to say so.
/// </summary>
public sealed class MountOverrideSemanticsTests
{
    private static readonly string[] NoMounts = [];

    [Fact]
    public void A_command_line_mount_replaces_the_settings_entry_of_the_same_index()
    {
        var effective = MountOverrideSemantics.ComputeEffectiveMounts(
            ["/srv/run:/workspace:rw"],
            ["/srv/configured:/workspace:ro"]);

        var entry = Assert.Single(effective);
        Assert.Equal(0, entry.Index);
        Assert.Equal("/srv/run:/workspace:rw", entry.Value);
        Assert.Equal(MountOrigin.CommandLine, entry.Origin);
        Assert.Equal("/srv/configured:/workspace:ro", entry.ReplacedSettingsMount);
        Assert.True(entry.OverridesSettings);
        Assert.Equal("Orkeon:FileSystem:Mounts:0", entry.ConfigurationKey);
    }

    [Fact]
    public void Settings_entries_past_the_last_command_line_index_stay_in_force()
    {
        var effective = MountOverrideSemantics.ComputeEffectiveMounts(
            ["/srv/run:/workspace:rw"],
            ["/srv/configured:/workspace:ro", "/srv/out:/output:rw"]);

        Assert.Equal(2, effective.Count);
        Assert.Equal(MountOrigin.CommandLine, effective[0].Origin);

        Assert.Equal(MountOrigin.Settings, effective[1].Origin);
        Assert.Equal("/srv/out:/output:rw", effective[1].Value);
        Assert.Null(effective[1].ReplacedSettingsMount);
        Assert.False(effective[1].OverridesSettings);
        Assert.Equal("Orkeon:FileSystem:Mounts:1", effective[1].ConfigurationKey);
    }

    [Fact]
    public void A_command_line_mount_past_the_settings_list_overrides_nothing()
    {
        var effective = MountOverrideSemantics.ComputeEffectiveMounts(
            ["/srv/a:/workspace:ro", "/srv/b:/output:rw"],
            ["/srv/configured:/workspace:ro"]);

        Assert.Equal(2, effective.Count);
        Assert.True(effective[0].OverridesSettings);
        Assert.Equal(MountOrigin.CommandLine, effective[1].Origin);
        Assert.Null(effective[1].ReplacedSettingsMount);
        Assert.False(effective[1].OverridesSettings);
    }

    [Fact]
    public void Without_command_line_mounts_the_settings_list_is_untouched()
    {
        var effective = MountOverrideSemantics.ComputeEffectiveMounts(
            NoMounts,
            ["/srv/configured:/workspace:ro", "/srv/out:/output:rw"]);

        Assert.Equal(2, effective.Count);
        Assert.All(effective, entry => Assert.Equal(MountOrigin.Settings, entry.Origin));
    }

    [Fact]
    public void The_configuration_key_matches_the_one_the_runner_writes()
    {
        Assert.Equal("Orkeon:FileSystem:Mounts:0", MountOverrideSemantics.ConfigurationKey(0));
        Assert.Equal("Orkeon:FileSystem:Mounts:12", MountOverrideSemantics.ConfigurationKey(12));
        Assert.Throws<ArgumentOutOfRangeException>(() => MountOverrideSemantics.ConfigurationKey(-1));
    }

    [Fact]
    public void The_explanations_state_the_override_and_the_whitelisting()
    {
        Assert.Contains("replaces", MountOverrideSemantics.Explanation, StringComparison.Ordinal);
        Assert.Contains(
            MountOverrideSemantics.ConfigurationSection,
            MountOverrideSemantics.Explanation,
            StringComparison.Ordinal);
        Assert.Contains(
            MountOverrideSemantics.ExternalMountsConfigurationSection,
            MountOverrideSemantics.ExternalMountsExplanation,
            StringComparison.Ordinal);
    }
}
