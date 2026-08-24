using System.Collections.ObjectModel;
using System.Globalization;
using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Teams;

/// <summary>Payload of the launch/resume requests: the team folder or session to act on.</summary>
public sealed class TeamActionEventArgs(string path) : EventArgs
{
    /// <summary>Absolute path of the team folder.</summary>
    public string Path { get; } = path;
}

/// <summary>Payload of a resume request: the stopped session to reopen.</summary>
public sealed class SessionResumeEventArgs(ForgeSolutionSummary session) : EventArgs
{
    /// <summary>The session, as the forge catalog listed it.</summary>
    public ForgeSolutionSummary Session { get; } = session;
}

/// <summary>One team card of "Mes équipes".</summary>
public sealed class TeamCardViewModel
{
    internal TeamCardViewModel(TeamSummary summary, TeamsViewModel owner, IStudioStrings strings)
    {
        Summary = summary;
        ScheduleDisplay = summary.Schedule switch
        {
            null or "" => strings[StudioStringKeys.TeamsOnDemand],
            "hourly" => strings[StudioStringKeys.TeamsHourly],
            var schedule when schedule.StartsWith("daily@", StringComparison.Ordinal) =>
                string.Format(CultureInfo.CurrentCulture, strings[StudioStringKeys.TeamsDaily], schedule["daily@".Length..]),
            var schedule => schedule,
        };
        LaunchCommand = new RelayCommand(() => owner.RequestLaunch(summary.Path));
        DuplicateCommand = new RelayCommand(() => owner.Duplicate(summary.Path));
        DeleteCommand = new RelayCommand(() => owner.Delete(summary.Path));
        OpenCommand = new RelayCommand(() => owner.OpenInShell(summary.Path), () => owner.CanOpenInShell);
    }

    /// <summary>"Ouvrir" — the team folder in the OS explorer (audit 03).</summary>
    public RelayCommand OpenCommand { get; }

    /// <summary>The team folder, as the catalog read it.</summary>
    public TeamSummary Summary { get; }

    /// <summary>Display name.</summary>
    public string Name => Summary.Name;

    /// <summary>Folder name — expert only.</summary>
    public string Slug => Summary.Slug;

    /// <summary>The need, in the user's words; empty for a folder without a sidecar.</summary>
    public string? Description => Summary.Description;

    /// <summary>Whether a description exists.</summary>
    public bool HasDescription => Summary.Description is { Length: > 0 };

    /// <summary>Name of the team's model profile, when one was chosen.</summary>
    public string? Profile => Summary.Profile;

    /// <summary>Whether a profile is recorded.</summary>
    public bool HasProfile => Summary.Profile is { Length: > 0 };

    /// <summary>The schedule in words ("À la demande", "Chaque jour à 07:30", …).</summary>
    public string ScheduleDisplay { get; }

    /// <summary>True for a team the wizard adopted (it carries the Studio sidecar).</summary>
    public bool IsAdopted => Summary.HasMetadata;

    /// <summary>True for a scheduled team — the card's green badge.</summary>
    public bool IsScheduled => Summary.Schedule is { Length: > 0 };

    /// <summary>Hands the folder to the launcher.</summary>
    public RelayCommand LaunchCommand { get; }

    /// <summary>Copies the folder next to itself.</summary>
    public RelayCommand DuplicateCommand { get; }

    /// <summary>Deletes the folder, recursively.</summary>
    public RelayCommand DeleteCommand { get; }
}

/// <summary>One resumable wizard session, listed under the teams.</summary>
public sealed class InProgressSessionViewModel
{
    internal InProgressSessionViewModel(ForgeSolutionSummary summary, TeamsViewModel owner)
    {
        Summary = summary;
        ResumeCommand = new RelayCommand(() => owner.RequestResume(summary));
    }

    /// <summary>The session, as the forge catalog listed it.</summary>
    public ForgeSolutionSummary Summary { get; }

    /// <summary>Display title, falling back to the slug.</summary>
    public string Title => Summary.Title is { Length: > 0 } title ? title : Summary.Slug;

    /// <summary>Wire state — expert only.</summary>
    public string State => Summary.State;

    /// <summary>Reopens the wizard where the session stopped.</summary>
    public RelayCommand ResumeCommand { get; }
}

/// <summary>
/// "Mes équipes" (design v3): every adopted team is an ordinary folder under the teams
/// root — copiable, deletable, runnable with <c>orkeon run</c> alone — plus the wizard
/// sessions still underway, resumable where they stopped.
/// </summary>
public sealed class TeamsViewModel : ObservableObject
{
    private readonly Func<IReadOnlyList<TeamSummary>> _loadTeams;
    private readonly IShellOpener? _shellOpener;
    private readonly Func<IReadOnlyList<ForgeSolutionSummary>> _loadSessions;
    private readonly IStudioStrings _strings;

    /// <summary>Builds the screen over its seams; the loaders default to the real catalogs.</summary>
    public TeamsViewModel(
        string? teamsRoot = null,
        string? workspaceDirectory = null,
        Func<IReadOnlyList<TeamSummary>>? loadTeams = null,
        Func<IReadOnlyList<ForgeSolutionSummary>>? loadSessions = null,
        IStudioStrings? strings = null,
        IShellOpener? shellOpener = null)
    {
        _shellOpener = shellOpener;
        var root = teamsRoot ?? TeamCatalog.DefaultRoot();
        var workspace = workspaceDirectory ?? Environment.CurrentDirectory;
        _loadTeams = loadTeams ?? (() => TeamCatalog.List(root));
        _loadSessions = loadSessions ?? (() => ForgeSessionCatalog.List(workspace));
        _strings = strings ?? EnglishStudioStrings.Instance;
        CreateCommand = new RelayCommand(() => CreateRequested?.Invoke(this, EventArgs.Empty));
        Refresh();
    }

    /// <summary>Raised when a team should land in the launcher.</summary>
    public event EventHandler<TeamActionEventArgs>? LaunchRequested;

    /// <summary>Raised when a stopped wizard session should resume.</summary>
    public event EventHandler<SessionResumeEventArgs>? ResumeRequested;

    /// <summary>Raised by "Créer une équipe" — the shell brings the wizard forward.</summary>
    public event EventHandler? CreateRequested;

    /// <summary>The team cards.</summary>
    public ObservableCollection<TeamCardViewModel> Teams { get; } = [];

    /// <summary>The wizard sessions still underway.</summary>
    public ObservableCollection<InProgressSessionViewModel> InProgress { get; } = [];

    /// <summary>Number of teams — the sidebar count.</summary>
    public int Count => Teams.Count;

    /// <summary>Whether the cards can offer "Ouvrir" at all (a shell opener was wired).</summary>
    public bool CanOpenInShell => _shellOpener is not null;

    internal void OpenInShell(string path) => _shellOpener?.Open(path);

    /// <summary>Whether any team exists.</summary>
    public bool IsEmpty => Teams.Count == 0;

    /// <summary>Whether resumable sessions are listed.</summary>
    public bool HasInProgress => InProgress.Count > 0;

    /// <summary>Opens the creation wizard.</summary>
    public RelayCommand CreateCommand { get; }

    /// <summary>Re-reads both catalogs.</summary>
    public void Refresh()
    {
        Teams.Clear();
        foreach (var team in _loadTeams())
            Teams.Add(new TeamCardViewModel(team, this, _strings));

        InProgress.Clear();
        foreach (var session in _loadSessions())
        {
            if (session.CanResume)
                InProgress.Add(new InProgressSessionViewModel(session, this));
        }

        OnPropertiesChanged(nameof(Count), nameof(IsEmpty), nameof(HasInProgress));
    }

    internal void RequestLaunch(string path) => LaunchRequested?.Invoke(this, new TeamActionEventArgs(path));

    internal void RequestResume(ForgeSolutionSummary summary) =>
        ResumeRequested?.Invoke(this, new SessionResumeEventArgs(summary));

    internal void Duplicate(string path)
    {
        if (TeamCatalog.Duplicate(path) is not null)
            Refresh();
    }

    internal void Delete(string path)
    {
        if (TeamCatalog.Delete(path))
            Refresh();
    }
}
