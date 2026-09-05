using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.History;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Wpf.ViewModels.Mounts;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Teams;

/// <summary>Payload of the launch/resume requests: the team folder or session to act on.</summary>
public sealed class TeamActionEventArgs(string path) : EventArgs
{
    /// <summary>Absolute path of the team folder.</summary>
    [SuppressMessage("Minor Code Smell", "S3604:Member initializer values should not be redundant",
        Justification = "False positive on a primary constructor: the initializer IS the only "
                      + "assignment of the member, and removing it would leave it unset.")]
    public string Path { get; } = path;
}

/// <summary>Payload of a « Changer les dossiers » request: the card whose mounts open.</summary>
public sealed class TeamMountsRequestedEventArgs(TeamCardViewModel card) : EventArgs
{
    /// <summary>The team card.</summary>
    [SuppressMessage("Minor Code Smell", "S3604:Member initializer values should not be redundant",
        Justification = "False positive on a primary constructor: the initializer IS the only "
                      + "assignment of the member, and removing it would leave it unset.")]
    public TeamCardViewModel Card { get; } = card;
}

/// <summary>Payload of a resume request: the stopped session to reopen.</summary>
public sealed class SessionResumeEventArgs(ForgeSolutionSummary session) : EventArgs
{
    /// <summary>The session, as the forge catalog listed it.</summary>
    [SuppressMessage("Minor Code Smell", "S3604:Member initializer values should not be redundant",
        Justification = "False positive on a primary constructor: the initializer IS the only "
                      + "assignment of the member, and removing it would leave it unset.")]
    public ForgeSolutionSummary Session { get; } = session;
}

/// <summary>Payload of a «Modifier» request (W-09): the team and the session that adopted it.</summary>
public sealed class TeamModifyEventArgs(TeamSummary team, ForgeSolutionSummary session) : EventArgs
{
    /// <summary>The adopted team, as the catalog listed it.</summary>
    [SuppressMessage("Minor Code Smell", "S3604:Member initializer values should not be redundant",
        Justification = "False positive on a primary constructor: the initializer IS the only "
                      + "assignment of the member, and removing it would leave it unset.")]
    public TeamSummary Team { get; } = team;

    /// <summary>The forge session whose <c>promotedTo</c> is the team folder.</summary>
    [SuppressMessage("Minor Code Smell", "S3604:Member initializer values should not be redundant",
        Justification = "False positive on a primary constructor: the initializer IS the only "
                      + "assignment of the member, and removing it would leave it unset.")]
    public ForgeSolutionSummary Session { get; } = session;
}

/// <summary>One mount chip of a team card: virtual path plus its rights, in words.</summary>
/// <param name="Label">What the screen shows: the virtual path and its rights, never a folder.</param>
/// <param name="IsReadWrite">Drives the folder-open / pencil icon.</param>
/// <param name="MountString">
/// The entry this chip stands for, verbatim — the command parameter a "remove" button needs.
/// Empty where nothing removes chips.
/// </param>
/// <param name="IsUndeclared">
/// True when the folder behind the chip is not in the Settings > Allowed folders list. The chip
/// then reads red: the settings are the list of what this machine allows, and a team reaching
/// outside it should not have to be discovered by reading a sidecar.
/// </param>
public sealed record TeamMountChip(
    string Label, bool IsReadWrite, string MountString = "", bool IsUndeclared = false);

/// <summary>One team card of the my-teams screen.</summary>
public sealed class TeamCardViewModel : ObservableObject
{
    private readonly IStudioStrings _strings;
    private DateTimeOffset? _lastRun;
    private RunOutcome? _lastOutcome;
    private bool _isConfirmingDelete;

    internal TeamCardViewModel(
        TeamSummary summary, TeamsViewModel owner, IStudioStrings strings, IReadOnlyList<string> declaredMounts)
    {
        _strings = strings;
        Summary = summary;

        // Never the raw string: it carries the physical folder, and a team card is an
        // agent-facing surface like any other (ADR-008).
        MountChips = [.. summary.Mounts.Select(mountString =>
        {
            var (label, readWrite) = MountLabels.Describe(mountString, strings);
            return new TeamMountChip(
                label, readWrite, IsUndeclared: !MountLabels.IsDeclared(mountString, declaredMounts));
        })];
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
        // Delete is a two-step gesture, and BOTH entry points (the labelled Novice button
        // and the Expert trash icon) arm the same confirmation: no path removes a team on
        // a single click. The banner replaces the action row in place — no MessageBox.
        AskDeleteCommand = new RelayCommand(() => owner.ArmDelete(this));
        ConfirmDeleteCommand = new RelayCommand(() => owner.Delete(summary.Path));
        CancelDeleteCommand = new RelayCommand(() => IsConfirmingDelete = false);
        OpenCommand = new RelayCommand(() => owner.OpenInShell(summary.Path), () => owner.CanOpenInShell);
        ChangeMountsCommand = new RelayCommand(() => owner.RequestMounts(this));
        ExportCommand = new RelayCommand(() => owner.Export(summary.Path));
        TestCommand = new RelayCommand(() => owner.RequestTest(summary.Path));
        // «Modifier» (W-09): only a team some session promoted can reopen the wizard —
        // an imported team, or one whose session is gone, keeps the button disabled with
        // the tooltip saying why. Resolved at card build; Refresh() rebuilds the cards.
        CanModify = owner.FindSessionFor(summary) is not null;
        ModifyCommand = new RelayCommand(() => owner.RequestModify(summary), () => CanModify);
    }

    /// <summary>« Modifier » — reopens the wizard at step 2 on this team (W-09).</summary>
    public RelayCommand ModifyCommand { get; }

    /// <summary>Whether a forge session points at this team folder.</summary>
    public bool CanModify { get; }

    /// <summary>« Changer les dossiers » — the team-mounts modal (remediation v2, F-02).</summary>
    public RelayCommand ChangeMountsCommand { get; }

    /// <summary>Copies the team folder somewhere for sharing, settings file left behind.</summary>
    public RelayCommand ExportCommand { get; }

    /// <summary>Hands the team to the expert trial screen.</summary>
    public RelayCommand TestCommand { get; }

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

    /// <summary>The schedule in words (on demand, every day at 07:30, …).</summary>
    public string ScheduleDisplay { get; }

    /// <summary>True for a team the wizard adopted (it carries the Studio sidecar).</summary>
    public bool IsAdopted => Summary.HasMetadata;

    /// <summary>True for a scheduled team — the card's green badge.</summary>
    public bool IsScheduled => Summary.Schedule is { Length: > 0 };

    /// <summary>Hands the folder to the launcher.</summary>
    public RelayCommand LaunchCommand { get; }

    /// <summary>Copies the folder next to itself.</summary>
    public RelayCommand DuplicateCommand { get; }

    /// <summary>Arms the in-place confirmation; deletes nothing on its own.</summary>
    public RelayCommand AskDeleteCommand { get; }

    /// <summary>Deletes the folder, recursively — only reachable from the armed banner.</summary>
    public RelayCommand ConfirmDeleteCommand { get; }

    /// <summary>Disarms the confirmation and puts the action row back.</summary>
    public RelayCommand CancelDeleteCommand { get; }

    /// <summary>Whether this card is showing its delete-this-team confirmation banner.</summary>
    public bool IsConfirmingDelete
    {
        get => _isConfirmingDelete;
        internal set
        {
            if (SetProperty(ref _isConfirmingDelete, value))
                OnPropertyChanged(nameof(IsIdle));
        }
    }

    /// <summary>The action row's own visibility — the banner takes its place, never sits over it.</summary>
    public bool IsIdle => !_isConfirmingDelete;

    /// <summary>The team's mount strings, straight from the sidecar.</summary>
    public IReadOnlyList<string> Mounts => Summary.Mounts;

    /// <summary>Whether any mount is recorded — the card's "Dossiers :" line.</summary>
    public bool HasMounts => Summary.Mounts.Count > 0;

    /// <summary>Agent definitions counted on disk; null when the folder shows none.</summary>
    public int? AgentCount => Summary.AgentCount;

    /// <summary>When this team last ran, from the launch history; null when it never did.</summary>
    public DateTimeOffset? LastRun => _lastRun;

    /// <summary>How that last run ended; null when the team never ran.</summary>
    public RunOutcome? LastOutcome => _lastOutcome;

    /// <summary>The mount chips, virtual path + rights in words.</summary>
    public IReadOnlyList<TeamMountChip> MountChips { get; }

    /// <summary>
    /// The card badge (mock: scheduled = green, to be tested = amber, on demand = accent).
    /// A team that never ran and is not scheduled still has to earn its first run.
    /// </summary>
    public string BadgeText =>
        IsScheduled || _lastRun is not null ? ScheduleDisplay : _strings[StudioStringKeys.TeamsToTest];

    /// <summary>ok / warn / accent — the badge's tone name for the view's triggers.</summary>
    public string BadgeTone => (IsScheduled, _lastRun) switch
    {
        (true, _) => "ok",
        (false, null) => "warn",
        _ => "accent",
    };

    /// <summary>The last-run date, or never-ran — the meta line's history part.</summary>
    public string LastRunDisplay
    {
        get
        {
            if (_lastRun is not { } startedAt)
                return _strings[StudioStringKeys.TeamsNeverRan];

            var outcome = _strings[_lastOutcome == RunOutcome.Success
                ? StudioStringKeys.TeamsRunOk
                : StudioStringKeys.TeamsRunFail];

            return string.Format(
                CultureInfo.CurrentCulture, _strings[StudioStringKeys.TeamsLastRun],
                startedAt.ToLocalTime().ToString("d", CultureInfo.CurrentCulture),
                outcome);
        }
    }

    /// <summary>« n agents » when the folder shows agent files.</summary>
    public string? AgentCountDisplay =>
        Summary.AgentCount is { } count
            ? string.Format(CultureInfo.CurrentCulture, _strings[StudioStringKeys.RunMetaAgents], count)
            : null;

    /// <summary>Whether the agent-count meta part exists.</summary>
    public bool HasAgentCount => Summary.AgentCount is not null;

    /// <summary>The setting-name part ("setting: X") — the meta line's model-profile part.</summary>
    public string? ProfileDisplay =>
        Summary.Profile is { Length: > 0 } profile
            ? string.Format(CultureInfo.CurrentCulture, _strings[StudioStringKeys.TeamsSettingLabel], profile)
            : null;

    /// <summary>
    /// The card's one meta line, « · »-joined like the launcher's (v3 F-02):
    /// "3 agents · every day at 07:30 · last run: … · setting: X".
    /// </summary>
    public string MetaLine => string.Join(
        " · ",
        new[] { AgentCountDisplay, ScheduleDisplay, LastRunDisplay, ProfileDisplay }
            .Where(part => !string.IsNullOrWhiteSpace(part)));

    internal void SetLastRun(DateTimeOffset? startedAt, RunOutcome? outcome)
    {
        if (_lastRun == startedAt && _lastOutcome == outcome)
            return;

        _lastRun = startedAt;
        _lastOutcome = outcome;
        OnPropertiesChanged(
            nameof(LastRun), nameof(LastOutcome), nameof(LastRunDisplay),
            nameof(BadgeText), nameof(BadgeTone), nameof(MetaLine));
    }
}

/// <summary>One resumable wizard session, listed under the teams.</summary>
public sealed class InProgressSessionViewModel : ObservableObject
{
    private bool _isConfirmingDelete;

    internal InProgressSessionViewModel(ForgeSolutionSummary summary, TeamsViewModel owner)
    {
        Summary = summary;
        ResumeCommand = new RelayCommand(() => owner.RequestResume(summary));
        AskDeleteCommand = new RelayCommand(() => owner.ArmDelete(this));
        ConfirmDeleteCommand = new RelayCommand(() => owner.DeleteSession(summary));
        CancelDeleteCommand = new RelayCommand(() => IsConfirmingDelete = false);
    }

    /// <summary>The session, as the forge catalog listed it.</summary>
    public ForgeSolutionSummary Summary { get; }

    /// <summary>Display title, falling back to the slug.</summary>
    public string Title => Summary.Title is { Length: > 0 } title ? title : Summary.Slug;

    /// <summary>Wire state — expert only.</summary>
    public string State => Summary.State;

    /// <summary>Reopens the wizard where the session stopped.</summary>
    public RelayCommand ResumeCommand { get; }

    /// <summary>Arms the in-place confirmation; deletes nothing on its own.</summary>
    public RelayCommand AskDeleteCommand { get; }

    /// <summary>Discards the abandoned draft, recursively.</summary>
    public RelayCommand ConfirmDeleteCommand { get; }

    /// <summary>Disarms the confirmation.</summary>
    public RelayCommand CancelDeleteCommand { get; }

    /// <summary>Whether this row is showing its confirmation banner.</summary>
    public bool IsConfirmingDelete
    {
        get => _isConfirmingDelete;
        internal set
        {
            if (SetProperty(ref _isConfirmingDelete, value))
                OnPropertyChanged(nameof(IsIdle));
        }
    }

    /// <summary>The action row's own visibility.</summary>
    public bool IsIdle => !_isConfirmingDelete;
}

/// <summary>
/// The my-teams screen's seams: the collaborators it otherwise builds itself. They travel as
/// one record rather than as eight constructor parameters — the screen has exactly one real
/// caller (the shell) and a row of tests, and every one of them names two or three of these
/// and leaves the rest to the real catalogs.
/// </summary>
public sealed record TeamsDependencies
{
    /// <summary>Where the adopted teams live; the default teams root when null.</summary>
    public string? TeamsRoot { get; init; }

    /// <summary>Where the wizard sessions live; the process working directory when null.</summary>
    public string? WorkspaceDirectory { get; init; }

    /// <summary>Reads the team folders; the real catalog under <see cref="TeamsRoot"/> when null.</summary>
    public Func<IReadOnlyList<TeamSummary>>? LoadTeams { get; init; }

    /// <summary>Reads the wizard sessions; the real forge catalog when null.</summary>
    public Func<IReadOnlyList<ForgeSolutionSummary>>? LoadSessions { get; init; }

    /// <summary>The localized strings; English when null.</summary>
    public IStudioStrings? Strings { get; init; }

    /// <summary>Opens a folder in the OS explorer; the cards offer no "Ouvrir" when null.</summary>
    public IShellOpener? ShellOpener { get; init; }

    /// <summary>The launch history the cards read their last run from; none when null.</summary>
    public ILaunchHistoryStore? HistoryStore { get; init; }

    /// <summary>The folders the settings declare; none when null.</summary>
    public Func<IReadOnlyList<string>>? DeclaredMounts { get; init; }
}

/// <summary>
/// The my-teams screen (design v3): every adopted team is an ordinary folder under the teams
/// root — copiable, deletable, runnable with <c>orkeon run</c> alone — plus the wizard
/// sessions still underway, resumable where they stopped.
/// </summary>
public sealed class TeamsViewModel : ObservableObject
{
    private readonly Func<IReadOnlyList<TeamSummary>> _loadTeams;
    private readonly Func<IReadOnlyList<string>> _declaredMounts;
    private readonly IShellOpener? _shellOpener;
    private readonly Func<IReadOnlyList<ForgeSolutionSummary>> _loadSessions;
    private readonly IStudioStrings _strings;
    private readonly ILaunchHistoryStore? _historyStore;
    private Dictionary<string, (DateTimeOffset StartedAt, RunOutcome Outcome)> _lastRuns = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Builds the screen over its seams; the loaders default to the real catalogs.</summary>
    public TeamsViewModel(TeamsDependencies? dependencies = null)
    {
        var wired = dependencies ?? new TeamsDependencies();
        _declaredMounts = wired.DeclaredMounts ?? (() => []);
        _shellOpener = wired.ShellOpener;
        _historyStore = wired.HistoryStore;
        var root = wired.TeamsRoot ?? TeamCatalog.DefaultRoot();
        var workspace = wired.WorkspaceDirectory ?? Environment.CurrentDirectory;
        _loadTeams = wired.LoadTeams ?? (() => TeamCatalog.List(root));
        _loadSessions = wired.LoadSessions ?? (() => ForgeSessionCatalog.List(workspace));
        _strings = wired.Strings ?? EnglishStudioStrings.Instance;
        CreateCommand = new RelayCommand(() => CreateRequested?.Invoke(this, EventArgs.Empty));
        ImportCommand = new RelayCommand(() => ImportRequested?.Invoke(this, EventArgs.Empty));
        Refresh();
    }

    /// <summary>Raised when a team should land in the launcher.</summary>
    public event EventHandler<TeamActionEventArgs>? LaunchRequested;

    /// <summary>Raised when a stopped wizard session should resume.</summary>
    public event EventHandler<SessionResumeEventArgs>? ResumeRequested;

    /// <summary>Raised by the create-a-team button — the shell brings the wizard forward.</summary>
    public event EventHandler? CreateRequested;

    /// <summary>
    /// Raised by the empty state's second way out — the shell brings the import screen
    /// forward. An empty My-teams offers both doors, not just the one.
    /// </summary>
    public event EventHandler? ImportRequested;

    /// <summary>Raised by « Changer les dossiers » — the shell opens the team-mounts modal.</summary>
    public event EventHandler<TeamMountsRequestedEventArgs>? MountsRequested;

    /// <summary>Raised by the card's Tester icon — the shell brings the trial screen forward.</summary>
    public event EventHandler<TeamActionEventArgs>? TestRequested;

    /// <summary>Raised by «Modifier» — the shell reopens the wizard on the team (W-09).</summary>
    public event EventHandler<TeamModifyEventArgs>? ModifyRequested;

    /// <summary>
    /// The session that adopted <paramref name="team"/>, or null — the reverse lookup by
    /// <c>promotedTo</c>, over the same session loader the in-progress list reads.
    /// </summary>
    internal ForgeSolutionSummary? FindSessionFor(TeamSummary team)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var target = NormalizePath(team.Path);
        if (target.Length == 0)
            return null;

        return _loadSessions().FirstOrDefault(session =>
            session.PromotedTo is { Length: > 0 } promoted
            && string.Equals(NormalizePath(promoted), target, comparison));
    }

    internal void RequestModify(TeamSummary team)
    {
        if (FindSessionFor(team) is { } session)
            ModifyRequested?.Invoke(this, new TeamModifyEventArgs(team, session));
    }

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

    /// <summary>Opens the import screen.</summary>
    public RelayCommand ImportCommand { get; }

    /// <summary>Re-reads both catalogs.</summary>
    public void Refresh()
    {
        Teams.Clear();
        // Read once per refresh, not once per card: the settings list is the same for all of them.
        var declared = _declaredMounts();
        foreach (var team in _loadTeams())
            Teams.Add(new TeamCardViewModel(team, this, _strings, declared));

        InProgress.Clear();
        foreach (var session in _loadSessions())
        {
            if (session.CanResume)
                InProgress.Add(new InProgressSessionViewModel(session, this));
        }

        ApplyLastRuns();
        OnPropertiesChanged(nameof(Count), nameof(IsEmpty), nameof(HasInProgress));
    }

    /// <summary>
    /// Reads the launch history once and stamps each card with its latest run. Refresh()
    /// stays synchronous and re-applies the cached map; the shell calls this at startup
    /// and after a run finishes. A missing store or an unreadable file degrades to cards
    /// that simply say nothing about past runs.
    /// </summary>
    public async Task LoadLastRunsAsync(CancellationToken cancellationToken = default)
    {
        if (_historyStore is null)
            return;

        try
        {
            var history = await _historyStore.LoadAsync(cancellationToken).ConfigureAwait(true);
            var map = new Dictionary<string, (DateTimeOffset, RunOutcome)>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in history.Entries)
            {
                var key = NormalizePath(entry.Target);
                if (key.Length == 0)
                    continue;
                if (!map.TryGetValue(key, out var known) || entry.StartedAt > known.Item1)
                    map[key] = (entry.StartedAt, entry.Outcome);
            }

            _lastRuns = map;
            ApplyLastRuns();
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            // History is comfort, not truth; the cards stay silent about past runs.
        }
    }

    private void ApplyLastRuns()
    {
        foreach (var card in Teams)
        {
            card.SetLastRun(
                _lastRuns.TryGetValue(NormalizePath(card.Summary.Path), out var run) ? run.StartedAt : null,
                _lastRuns.TryGetValue(NormalizePath(card.Summary.Path), out var known) ? known.Outcome : null);
        }
    }

    private static string NormalizePath(string path) => TeamCatalog.NormalizePath(path);

    internal void RequestLaunch(string path) => LaunchRequested?.Invoke(this, new TeamActionEventArgs(path));

    internal void RequestResume(ForgeSolutionSummary summary) =>
        ResumeRequested?.Invoke(this, new SessionResumeEventArgs(summary));

    internal void RequestMounts(TeamCardViewModel card) =>
        MountsRequested?.Invoke(this, new TeamMountsRequestedEventArgs(card));

    internal void RequestTest(string path) => TestRequested?.Invoke(this, new TeamActionEventArgs(path));

    internal void Export(string path)
    {
        if (ExportDestinationPicker?.Invoke() is not { Length: > 0 } destination)
            return;

        // Said either way (review D10): a refused export (existing destination, disk)
        // that looks identical to a successful one teaches the user nothing.
        StatusMessage = TeamCatalog.ExportTo(path, destination) is { } exported
            ? string.Format(CultureInfo.CurrentCulture, _strings[StudioStringKeys.TeamsExportedTo], exported)
            : _strings[StudioStringKeys.TeamsExportFailed];
    }

    /// <summary>Outcome line of the screen's last action (export); empty when quiet.</summary>
    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    private string _statusMessage = "";

    /// <summary>The export destination chooser — wired by the shell to the OS folder browser.</summary>
    public Func<string?>? ExportDestinationPicker { get; set; }

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

    /// <summary>
    /// Discards an abandoned wizard draft. The session directory is the whole of it —
    /// a draft that never promoted owns nothing else.
    /// </summary>
    internal void DeleteSession(ForgeSolutionSummary session)
    {
        if (ForgeSessionCatalog.Delete(session.Directory))
            Refresh();
    }

    /// <summary>
    /// Arms one card's confirmation and disarms every other. The mock keeps a single
    /// index rather than a flag per row: two banners open at once would ask the same
    /// question twice, and the second answer would land on the wrong team.
    /// </summary>
    internal void ArmDelete(object row)
    {
        foreach (var card in Teams)
            card.IsConfirmingDelete = ReferenceEquals(card, row);

        foreach (var session in InProgress)
            session.IsConfirmingDelete = ReferenceEquals(session, row);
    }
}
