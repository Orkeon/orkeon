using System.Collections.ObjectModel;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Wpf.ViewModels.Launch;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Teams;

/// <summary>
/// The expert "Tester" screen (design v3): a blank-run validation and a real trial over a
/// team, without touching the launch history — a trial is a rehearsal, not a run to replay.
/// It is a thin skin over a second <see cref="LaunchTabViewModel"/>: the same session, the
/// same argument builder, the same console as the real launcher, by construction.
/// </summary>
public sealed class TestTeamViewModel : ObservableObject
{
    private readonly Func<IReadOnlyList<TeamSummary>> _loadTeams;
    private TeamSummary? _selectedTeam;

    /// <summary>Builds the screen over the trial launcher.</summary>
    /// <param name="launcher">The dedicated launcher (built by the shell with no history store).</param>
    /// <param name="teamsRoot">Root of the teams directory; the default when null.</param>
    /// <param name="loadTeams">Catalog seam for tests.</param>
    public TestTeamViewModel(
        LaunchTabViewModel launcher,
        string? teamsRoot = null,
        Func<IReadOnlyList<TeamSummary>>? loadTeams = null)
    {
        ArgumentNullException.ThrowIfNull(launcher);

        Launcher = launcher;
        var root = teamsRoot ?? TeamCatalog.DefaultRoot();
        _loadTeams = loadTeams ?? (() => TeamCatalog.List(root));
        RefreshTeams();
    }

    /// <summary>The trial launcher — target, dry-run, run, console.</summary>
    public LaunchTabViewModel Launcher { get; }

    /// <summary>The teams offered by the picker.</summary>
    public ObservableCollection<TeamSummary> TeamChoices { get; } = [];

    /// <summary>The picked team; picking one aims the launcher at its folder.</summary>
    public TeamSummary? SelectedTeam
    {
        get => _selectedTeam;
        set
        {
            if (!SetProperty(ref _selectedTeam, value) || value is null)
                return;

            Launcher.Target.SelectedPath = value.Path;
        }
    }

    /// <summary>Re-reads the catalog (called when the teams list changes).</summary>
    public void RefreshTeams()
    {
        TeamChoices.Clear();
        foreach (var team in _loadTeams())
            TeamChoices.Add(team);
    }
}
