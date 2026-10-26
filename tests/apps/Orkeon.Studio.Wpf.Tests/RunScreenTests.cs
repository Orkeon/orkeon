using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Launch;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>The Launch screen (refonte, lot 4): the Run and Test screens merged behind one Trial | Real switch.</summary>
public sealed class RunScreenTests
{
    private const string Veille = "/teams/veille";
    private const string Synthese = "/teams/synthese";

    private static LaunchTabViewModel Launcher(FakeTargetProbe probe, bool trial = false) =>
        new(new LaunchTabDependencies
        {
            IsTrial = trial,
            ProcessRunner = new OrkeonProcessRunner(
                new FakeProcessLauncher(), new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
            TargetProbe = probe,
            Directories = new FakeDirectoryProbe(),
            Picker = new FakePathPicker(),
            SettingsStore = new FakeAppSettingsStore(),
        });

    private static RunScreenViewModel Screen()
    {
        var probe = new FakeTargetProbe();
        probe.Directories.Add(Veille);
        probe.Directories.Add(Synthese);
        return new RunScreenViewModel(
            Launcher(probe),
            Launcher(probe, trial: true),
            teamsRoot: "/teams",
            loadTeams: () =>
            [
                new TeamSummary { Name = "Synthèse", Slug = "synthese", Path = Synthese },
                new TeamSummary { Name = "Veille", Slug = "veille", Path = Veille },
            ]);
    }

    [Fact]
    public void The_screen_opens_on_the_real_launcher_and_the_switch_shows_the_trial()
    {
        var screen = Screen();
        Assert.False(screen.IsTrial);
        Assert.Same(screen.Real, screen.Active);

        screen.ShowTrialCommand.Execute(null);

        Assert.True(screen.IsTrial);
        Assert.False(screen.IsReal);
        Assert.Same(screen.Trial, screen.Active);

        screen.ShowRealCommand.Execute(null);
        Assert.Same(screen.Real, screen.Active);
    }

    [Fact]
    public void A_card_trial_aims_the_trial_launcher_only_and_the_picker_names_the_team()
    {
        var screen = Screen();

        screen.ShowTrial(Veille);

        Assert.True(screen.IsTrial);
        Assert.Equal(Veille, screen.Trial.Target.SelectedPath);
        Assert.Equal("", screen.Real.Target.SelectedPath);
        Assert.Equal("veille", screen.SelectedTeam?.Slug);

        // Back in real mode, the picker names the real launcher's team: none yet.
        screen.IsTrial = false;
        Assert.Null(screen.SelectedTeam);
    }

    [Fact]
    public void Picking_a_team_aims_the_launcher_the_screen_shows()
    {
        var screen = Screen();
        screen.ShowReal(Veille);

        screen.SelectedTeam = screen.TeamChoices.Single(team => team.Slug == "synthese");

        Assert.Equal(Synthese, screen.Real.Target.SelectedPath);
        Assert.Equal("", screen.Trial.Target.SelectedPath);
    }

    [Fact]
    public void The_trial_launcher_says_launch_the_trial_and_the_real_one_launch_now()
    {
        var screen = Screen();

        Assert.Equal(EnglishStudioStrings.Instance[StudioStringKeys.RunButtonTrial], screen.Trial.RunButtonLabel);
        Assert.Equal(EnglishStudioStrings.Instance[StudioStringKeys.RunButtonLaunch], screen.Real.RunButtonLabel);
        Assert.True(screen.Trial.IsTrial);
        Assert.False(screen.Real.IsTrial);
    }
}
