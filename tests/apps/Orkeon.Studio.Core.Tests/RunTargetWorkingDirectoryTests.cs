using Orkeon.Studio.Core.Targets;

namespace Orkeon.Studio.Core.Tests;

/// <summary>
/// Where the CLI is launched from, for each shape of target.
/// <para>
/// Both launchers derive it here now. They used to derive it separately, and the two answers
/// diverged the moment the ADR-008 detector started descending into a promoted team's
/// <c>crew/</c>: the WPF launcher was corrected to read <c>SelectedPath</c>, the TUI kept
/// reading <c>RunPath</c> and so started the CLI inside <c>crew/</c> — where the sidecar, the
/// appsettings and the team's own <c>output/</c> are not.
/// </para>
/// </summary>
public sealed class RunTargetWorkingDirectoryTests
{
    private static string P(params string[] parts) => Path.Combine([Path.GetTempPath(), .. parts]);

    [Fact]
    public void A_promoted_team_runs_from_the_team_folder_not_from_its_crew_subfolder()
    {
        var team = P("teams", "veille");
        var target = new RunTarget
        {
            Kind = RunTargetKind.MultiFileCrewDirectory,
            SelectedPath = team,
            RunPath = Path.Combine(team, RunTargetDetector.PromotedCrewDirectoryName),
        };

        Assert.Equal(team, target.WorkingDirectory);
    }

    [Fact]
    public void A_crew_directory_picked_directly_runs_from_itself()
    {
        var directory = P("crews", "veille");
        var target = new RunTarget
        {
            Kind = RunTargetKind.MultiFileCrewDirectory,
            SelectedPath = directory,
            RunPath = directory,
        };

        Assert.Equal(directory, target.WorkingDirectory);
    }

    [Fact]
    public void A_script_directory_runs_from_itself_even_though_its_run_path_is_a_file()
    {
        var directory = P("scripts", "veille");
        var target = new RunTarget
        {
            Kind = RunTargetKind.ScriptDirectory,
            SelectedPath = directory,
            RunPath = Path.Combine(directory, "crew.ork.ts"),
        };

        Assert.Equal(directory, target.WorkingDirectory);
    }

    [Theory]
    [InlineData(RunTargetKind.YamlFile, "crew.yaml")]
    [InlineData(RunTargetKind.ScriptFile, "crew.ork.ts")]
    public void A_file_target_runs_from_the_folder_holding_it(RunTargetKind kind, string fileName)
    {
        var directory = P("crews");
        var file = Path.Combine(directory, fileName);
        var target = new RunTarget { Kind = kind, SelectedPath = file, RunPath = file };

        Assert.Equal(directory, target.WorkingDirectory);
    }
}
