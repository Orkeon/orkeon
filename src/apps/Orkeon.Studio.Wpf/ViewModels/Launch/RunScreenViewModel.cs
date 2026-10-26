using System.Collections.ObjectModel;
using System.ComponentModel;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Launch;

/// <summary>
/// The Launch screen (refonte, lot 4): the former Run and Test screens merged behind one
/// Trial | Real switch. Each mode keeps its own launcher, so nothing the engine sees changes:
/// the real launcher records its runs in the history, the trial launcher is built without a
/// history store and stamps no last run - a trial is a rehearsal, not a run to replay. Both can
/// run at once, as before: the switch only says which one the screen shows.
/// </summary>
public sealed class RunScreenViewModel : ObservableObject
{
    private readonly Func<IReadOnlyList<TeamSummary>> _loadTeams;
    private TeamSummary? _selectedTeam;
    private bool _isTrial;

    /// <summary>Builds the screen over its two launchers.</summary>
    /// <param name="real">The launcher whose runs go to the history.</param>
    /// <param name="trial">The launcher built by the shell with no history store.</param>
    /// <param name="teamsRoot">Root of the teams directory; the default when null.</param>
    /// <param name="loadTeams">Catalog seam for tests.</param>
    public RunScreenViewModel(
        LaunchTabViewModel real,
        LaunchTabViewModel trial,
        string? teamsRoot = null,
        Func<IReadOnlyList<TeamSummary>>? loadTeams = null)
    {
        ArgumentNullException.ThrowIfNull(real);
        ArgumentNullException.ThrowIfNull(trial);

        Real = real;
        Trial = trial;
        var root = teamsRoot ?? TeamCatalog.DefaultRoot();
        TeamsRoot = root;
        // The active teams only (STUDIO-31, D-08): an archived team is neither launched nor tried from the list.
        _loadTeams = loadTeams ?? (() => TeamCatalog.List(root, TeamListFilter.Active));
        ShowRealCommand = new RelayCommand(() => IsTrial = false);
        ShowTrialCommand = new RelayCommand(() => IsTrial = true);
        Real.Target.PropertyChanged += (_, e) => OnTargetChanged(Real, e);
        Trial.Target.PropertyChanged += (_, e) => OnTargetChanged(Trial, e);
        RefreshTeams();
    }

    /// <summary>The launcher of a real run - the one the history records.</summary>
    public LaunchTabViewModel Real { get; }

    /// <summary>The launcher of a trial - no history, no last run.</summary>
    public LaunchTabViewModel Trial { get; }

    /// <summary>The teams root the picker lists (STUDIO-61): the one the window resolved, or the default.</summary>
    public string TeamsRoot { get; }

    /// <summary>True while the screen shows the trial launcher.</summary>
    public bool IsTrial
    {
        get => _isTrial;
        set
        {
            if (!SetProperty(ref _isTrial, value))
                return;

            OnPropertyChanged(nameof(IsReal));
            OnPropertyChanged(nameof(Active));
            SyncSelectedTeam();
        }
    }

    /// <summary>True while the screen shows the real launcher.</summary>
    public bool IsReal => !_isTrial;

    /// <summary>The launcher the screen shows.</summary>
    public LaunchTabViewModel Active => _isTrial ? Trial : Real;

    /// <summary>Shows the real launcher.</summary>
    public RelayCommand ShowRealCommand { get; }

    /// <summary>Shows the trial launcher.</summary>
    public RelayCommand ShowTrialCommand { get; }

    /// <summary>The teams offered by the picker of the team card.</summary>
    public ObservableCollection<TeamSummary> TeamChoices { get; } = [];

    /// <summary>
    /// The picked team; picking one aims the shown launcher at its folder and resolves it, the way
    /// a card's Launch or Try does - a path set without its detection left the buttons off.
    /// </summary>
    public TeamSummary? SelectedTeam
    {
        get => _selectedTeam;
        set
        {
            if (!SetProperty(ref _selectedTeam, value) || value is null)
                return;

            if (!SamePath(Active.Target.SelectedPath, value.Path))
                Active.Target.Select(value.Path);
        }
    }

    /// <summary>
    /// Re-reads the catalog - called when the teams list changes: on arrival in My teams, after an
    /// adoption or an import, and after an archive or a restore (STUDIO-31, D-08).
    /// </summary>
    public void RefreshTeams()
    {
        TeamChoices.Clear();
        foreach (var team in _loadTeams())
            TeamChoices.Add(team);
        SyncSelectedTeam();
    }

    /// <summary>Aims the real launcher at a team and shows it.</summary>
    public void ShowReal(string path)
    {
        Real.Target.Select(path);
        IsTrial = false;
    }

    /// <summary>Aims the trial launcher at a team and shows it.</summary>
    public void ShowTrial(string path)
    {
        Trial.Target.Select(path);
        IsTrial = true;
    }

    private void OnTargetChanged(LaunchTabViewModel launcher, PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(launcher, Active) && e.PropertyName == nameof(TargetSelectionViewModel.SelectedPath))
            SyncSelectedTeam();
    }

    // The picker follows the shown launcher's target, whoever set it (a card, the import, the
    // path box): it names the team that will run, or nothing when the target is not a listed team.
    private void SyncSelectedTeam()
    {
        var path = Active.Target.SelectedPath;
        var match = TeamChoices.FirstOrDefault(team => SamePath(team.Path, path));
        if (ReferenceEquals(match, _selectedTeam))
            return;

        _selectedTeam = match;
        OnPropertyChanged(nameof(SelectedTeam));
    }

    private static bool SamePath(string? left, string? right) =>
        !string.IsNullOrEmpty(left) && !string.IsNullOrEmpty(right)
        && string.Equals(
            left.TrimEnd('/', '\\'), right.TrimEnd('/', '\\'),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
