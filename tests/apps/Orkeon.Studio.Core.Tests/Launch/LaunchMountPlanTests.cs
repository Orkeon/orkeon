using Orkeon.Studio.Core.Launch;
using Orkeon.Studio.Core.Targets;

namespace Orkeon.Studio.Core.Tests.Launch;

/// <summary>
/// Which of a team's folders a launch lays as <c>--mount</c> (STUDIO-15 D-05). The chooser
/// records a settings entry verbatim in the sidecar, so every adopted team carries the
/// machine's own defaults: passing those back is noise the runner now tolerates and an older
/// runner refused outright. Anything the settings do not already hold — another name, another
/// folder, other rights — is the team's own intent and goes on the command line.
/// </summary>
public sealed class LaunchMountPlanTests
{
    private static readonly string[] Settings =
    [
        "/data/invoices:/workspace:ro",
        "/data/out:/output:rw",
        "/data/docs:/docs:ro",
    ];

    private static RunTarget YamlTarget() => new()
    {
        Kind = RunTargetKind.YamlFile,
        SelectedPath = "/teams/veille/crew.yaml",
        RunPath = "/teams/veille/crew.yaml",
    };

    [Fact]
    public void A_sidecar_folder_also_declared_under_the_same_name_is_laid_once()
    {
        // The two entries the chooser copied from the settings, plus the team's own output.
        var team = new[] { "/data/invoices:/workspace:ro", "/data/out:/output:rw", "/teams/veille/output:/result:rw" };

        var laid = LaunchMountPlan.WithoutSettingsDuplicates(team, Settings);

        Assert.Equal(["/teams/veille/output:/result:rw"], laid);
    }

    [Fact]
    public void The_same_folder_under_another_name_is_laid_and_shown_as_a_replacement()
    {
        // The invoices folder, which the settings spend on /workspace, under the name /docs —
        // which the settings spend on another folder.
        var team = new[] { "/data/invoices:/docs:ro" };

        var laid = LaunchMountPlan.WithoutSettingsDuplicates(team, Settings);

        Assert.Equal(team, laid);

        var effective = MountOverrideSemantics.ComputeEffectiveMounts(
            laid, Settings, MountAutoInjection.For(YamlTarget()));
        var row = Assert.Single(effective, m => m.Origin == MountOrigin.CommandLine);
        Assert.Equal(2, row.Index);
        Assert.True(row.OverridesSettings);
        Assert.Equal("/data/docs:/docs:ro", row.ReplacedSettingsMount);
    }

    [Fact]
    public void The_same_folder_and_name_with_other_rights_is_laid_as_the_teams_own_intent()
    {
        // The settings say read-only; the team was adopted with write access on the same
        // root. That --mount replaces the settings entry for the run (D-01), and the table
        // shows the replacement — dropping it would silently narrow what the team was given.
        var laid = LaunchMountPlan.WithoutSettingsDuplicates(["/data/invoices:/workspace:rw"], Settings);

        Assert.Equal(["/data/invoices:/workspace:rw"], laid);
    }

    [Fact]
    public void A_trailing_separator_or_slash_does_not_hide_a_duplicate()
    {
        var laid = LaunchMountPlan.WithoutSettingsDuplicates(
            ["/data/invoices/:/workspace/:ro"], Settings);

        Assert.Empty(laid);
    }

    [Fact]
    public void An_unreadable_team_entry_passes_through()
    {
        // The runner reports it with its own message; dropping it here would hide that message.
        var laid = LaunchMountPlan.WithoutSettingsDuplicates(
            ["this is not a mount", "/data/out:/output:rw"], Settings);

        Assert.Equal(["this is not a mount"], laid);
    }

    [Fact]
    public void With_no_settings_every_team_entry_is_laid_in_order()
    {
        var team = new[] { "/data/out:/output:rw", "/data/invoices:/workspace:ro" };

        Assert.Equal(team, LaunchMountPlan.WithoutSettingsDuplicates(team, []));
    }
}
