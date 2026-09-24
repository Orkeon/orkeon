using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Orkeon.Domain.FileSystem;
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

/// <summary>Payload of a "Change the folders" request: the card whose mounts open.</summary>
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

/// <summary>Payload of a discard: the session whose directory is gone.</summary>
public sealed class SessionDeletedEventArgs(ForgeSolutionSummary session) : EventArgs
{
    /// <summary>The session, as the forge catalog listed it before the delete.</summary>
    [SuppressMessage("Minor Code Smell", "S3604:Member initializer values should not be redundant",
        Justification = "False positive on a primary constructor: the initializer IS the only "
                      + "assignment of the member, and removing it would leave it unset.")]
    public ForgeSolutionSummary Session { get; } = session;
}

/// <summary>
/// Payload of a «Modifier» request (W-09): the team, and nothing else. Which session it reopens
/// is the engine's answer (STUDIO-25, D-04): the wizard runs <c>forge reopen</c> on the folder,
/// which finds the session rule R links it to, or rebuilds one (FORGE-09).
/// </summary>
public sealed class TeamModifyEventArgs(TeamSummary team) : EventArgs
{
    /// <summary>The adopted team, as the catalog listed it.</summary>
    [SuppressMessage("Minor Code Smell", "S3604:Member initializer values should not be redundant",
        Justification = "False positive on a primary constructor: the initializer IS the only "
                      + "assignment of the member, and removing it would leave it unset.")]
    public TeamSummary Team { get; } = team;
}

/// <summary>
/// Payload of a rename that stands (STUDIO-28): the folder the team had, and the one it has now.
/// The shell lets the screens aimed at the former one follow it.
/// </summary>
public sealed class TeamRenamedEventArgs(string from, string path) : EventArgs
{
    /// <summary>The team folder before the rename.</summary>
    [SuppressMessage("Minor Code Smell", "S3604:Member initializer values should not be redundant",
        Justification = "False positive on a primary constructor: the initializer IS the only "
                      + "assignment of the member, and removing it would leave it unset.")]
    public string From { get; } = from;

    /// <summary>The team folder now.</summary>
    [SuppressMessage("Minor Code Smell", "S3604:Member initializer values should not be redundant",
        Justification = "False positive on a primary constructor: the initializer IS the only "
                      + "assignment of the member, and removing it would leave it unset.")]
    public string Path { get; } = path;
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
    private readonly TeamsViewModel _owner;
    private DateTimeOffset? _lastRun;
    private RunOutcome? _lastOutcome;
    private bool _isConfirmingDelete;
    private bool _isDescriptionExpanded;
    private TeamScheduleState _scheduleState;
    private string _scheduleMessage = "";
    private string? _scheduleManualCommand;
    private ForgeSolutionSummary? _linkedSession;
    private bool _deleteSessionToo = true;
    private string _deleteRefusal = "";
    private string? _deleteManualCommand;
    private bool _isRenaming;
    private string _renameText = "";
    private string _renameRefusal = "";

    internal TeamCardViewModel(
        TeamSummary summary, TeamsViewModel owner, IStudioStrings strings, IReadOnlyList<string> declaredMounts)
    {
        _strings = strings;
        _owner = owner;
        Summary = summary;

        // Never the raw string: it carries the physical folder, and a team card is an
        // agent-facing surface like any other (ADR-008). Red is for a folder vouched for by
        // nothing — neither declared nor the team's own (STUDIO-14, D-08): a team's /output
        // used to read red on its own card while the launcher let it through without a word.
        MountChips = [.. summary.Mounts.Select(mountString =>
        {
            var (label, readWrite) = MountLabels.Describe(mountString, strings);
            return new TeamMountChip(
                label, readWrite, IsUndeclared: !DeclaredMounts.IsVouchedFor(mountString, declaredMounts, summary.Path));
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
        // STUDIO-27 (D-06/D-07): the schedule goes first, then the folder, then — when the box
        // is left ticked — the workshop session rule R links to it.
        ConfirmDeleteCommand = new AsyncRelayCommand(() => owner.DeleteAsync(this));
        CancelDeleteCommand = new RelayCommand(() => IsConfirmingDelete = false);
        // STUDIO-27 (D-05): the engine installs and removes; the card shows what it answered.
        InstallScheduleCommand = new AsyncRelayCommand(() => owner.InstallScheduleAsync(this));
        StopScheduleCommand = new AsyncRelayCommand(() => owner.StopScheduleAsync(this));
        OpenCommand = new RelayCommand(() => owner.OpenInShell(summary.Path), () => owner.CanOpenInShell);
        ChangeMountsCommand = new RelayCommand(() => owner.RequestMounts(this));
        ExportCommand = new RelayCommand(() => owner.Export(summary.Path));
        TestCommand = new RelayCommand(() => owner.RequestTest(summary.Path));
        // «Modifier» (W-09, FORGE-09, STUDIO-25): always through `forge reopen` on the folder —
        // the engine finds the session rule R links it to, or rebuilds one from the team's own
        // crew/, so an imported team, or one whose session is gone, is modifiable again and not
        // only relaunchable. The card never looks the session up: it only knows whether the
        // folder names one (its forge.json carries an id) or can be read back (a YAML crew). A
        // team with neither keeps the button disabled, the tooltip saying why. Resolved at card
        // build; Refresh() rebuilds the cards.
        CanModify = TeamsViewModel.CanReopen(summary);
        string modifyTipKey;
        if (summary.ForgeSessionId is not null)
            modifyTipKey = StudioStringKeys.TeamsModifyTip;
        else if (summary.HasYamlCrew)
            modifyTipKey = StudioStringKeys.TeamsModifyRebuild;
        else
            modifyTipKey = StudioStringKeys.TeamsModifyNoSession;
        ModifyTooltip = strings[modifyTipKey];
        ModifyCommand = new RelayCommand(() => owner.RequestModify(summary), () => CanModify);
        ToggleDescriptionCommand = new RelayCommand(() => IsDescriptionExpanded = !IsDescriptionExpanded);
        // STUDIO-28 (D-07): « Rename » opens an editor in place of the action row, like the delete
        // banner — no MessageBox. The engine renames the folder and everything that follows it.
        RenameCommand = new RelayCommand(() => owner.BeginRename(this), () => owner.CanRename);
        ConfirmRenameCommand = new AsyncRelayCommand(() => owner.RenameAsync(this), () => CanConfirmRename);
        CancelRenameCommand = new RelayCommand(() => IsRenaming = false);
    }

    /// <summary>« Rename » (STUDIO-28, D-07): opens the editor on the team's name, in place of the action row.</summary>
    public RelayCommand RenameCommand { get; }

    /// <summary>Renames the team <see cref="RenameText"/> — the engine moves the folder and all that follows it, or nothing.</summary>
    public AsyncRelayCommand ConfirmRenameCommand { get; }

    /// <summary>Closes the editor; nothing is renamed.</summary>
    public RelayCommand CancelRenameCommand { get; }

    /// <summary>Whether this card shows its rename editor — the action row's place, never over it.</summary>
    public bool IsRenaming
    {
        get => _isRenaming;
        internal set
        {
            if (!SetProperty(ref _isRenaming, value))
                return;

            OnPropertyChanged(nameof(IsIdle));
            // A refusal belongs to the attempt it answered: a closed editor forgets it.
            if (!value)
                RefuseRename("");
        }
    }

    /// <summary>The new name, as typed; the editor opens on the current one.</summary>
    public string RenameText
    {
        get => _renameText;
        set
        {
            if (SetProperty(ref _renameText, value ?? ""))
                ConfirmRenameCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Whether the typed name says something: one that strips to nothing names no folder.</summary>
    public bool CanConfirmRename => TeamCatalog.TryNormalizeName(_renameText, out _);

    /// <summary>Why the team was not renamed — busy, the name taken, the engine's refusal; empty otherwise.</summary>
    public string RenameRefusal
    {
        get => _renameRefusal;
        private set
        {
            if (SetProperty(ref _renameRefusal, value))
                OnPropertyChanged(nameof(HasRenameRefusal));
        }
    }

    /// <summary>Whether the editor says why the team was not renamed.</summary>
    public bool HasRenameRefusal => _renameRefusal.Length > 0;

    /// <summary>Says why the team was not renamed; empty clears it.</summary>
    internal void RefuseRename(string refusal) => RenameRefusal = refusal;

    /// <summary>« Modifier » — reopens the wizard at step 2 on this team (W-09).</summary>
    public RelayCommand ModifyCommand { get; }

    /// <summary>Whether the wizard can reopen on this team: its forge.json names a session, or its crew can be read back.</summary>
    public bool CanModify { get; }

    /// <summary>The Modify button's tooltip: reopen, reopen through a rebuilt session, or why neither is possible.</summary>
    public string ModifyTooltip { get; }

    /// <summary>"Change the folders" — the team-mounts modal (remediation v2, F-02).</summary>
    public RelayCommand ChangeMountsCommand { get; }

    /// <summary>Copies the team folder somewhere for sharing, settings file left behind.</summary>
    public RelayCommand ExportCommand { get; }

    /// <summary>Hands the team to the expert trial screen.</summary>
    public RelayCommand TestCommand { get; }

    /// <summary>"Ouvrir" — the team folder in the OS explorer (audit 03).</summary>
    public RelayCommand OpenCommand { get; }

    /// <summary>The team folder, as the catalog read it.</summary>
    public TeamSummary Summary { get; }

    /// <summary>
    /// Display name — one line, whatever the sidecar says (STUDIO-16, D-01): a WPF TextBlock
    /// renders line breaks even without wrapping, so a pasted page in <c>name</c> used to
    /// become a forty-line card title. The tooltip carries this, never the raw text.
    /// </summary>
    public string Name => TeamCatalog.NormalizeName(Summary.Name);

    /// <summary>Folder name — expert only.</summary>
    public string Slug => Summary.Slug;

    /// <summary>The need, whole, in the user's words; empty for a folder without a sidecar.</summary>
    public string? Description => Summary.Description;

    /// <summary>The need cut to one paragraph — what the folded card shows (STUDIO-16, D-03).</summary>
    public string? DescriptionSummary => Summary.Summary;

    /// <summary>Whether a description exists.</summary>
    public bool HasDescription => Summary.Description is { Length: > 0 };

    /// <summary>
    /// What the card's description block shows: the summary folded, the whole need unfolded
    /// (STUDIO-16, D-02). The view bounds the folded block to three lines on top of this.
    /// </summary>
    public string? DescriptionDisplay => _isDescriptionExpanded ? Description : DescriptionSummary;

    /// <summary>
    /// Whether the folded card hides part of the need — the summary is not the whole text
    /// once whitespace is collapsed: a second paragraph, a cut at 240 characters, markup
    /// stripped. The « Voir plus » link exists only then: a short need has no toggle.
    /// </summary>
    public bool DescriptionOverflows =>
        Description is { Length: > 0 } description
        && !string.Equals(
            DescriptionSummary,
            string.Join(' ', description.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)),
            StringComparison.Ordinal);

    /// <summary>Whether the card shows the whole need rather than its summary.</summary>
    public bool IsDescriptionExpanded
    {
        get => _isDescriptionExpanded;
        set
        {
            if (SetProperty(ref _isDescriptionExpanded, value))
                OnPropertyChanged(nameof(DescriptionDisplay));
        }
    }

    /// <summary>« Voir plus » / « Voir moins » — unfolds and folds the need.</summary>
    public RelayCommand ToggleDescriptionCommand { get; }

    /// <summary>Name of the team's model profile, when one was chosen.</summary>
    public string? Profile => Summary.Profile;

    /// <summary>Whether a profile is recorded.</summary>
    public bool HasProfile => Summary.Profile is { Length: > 0 };

    /// <summary>The schedule in words (on demand, every day at 07:30, …).</summary>
    public string ScheduleDisplay { get; }

    /// <summary>True for a team the wizard adopted (it carries the Studio sidecar).</summary>
    public bool IsAdopted => Summary.HasMetadata;

    /// <summary>True for a team whose sidecar declares a schedule — what the user chose, not what runs.</summary>
    public bool IsScheduled => Summary.Schedule is { Length: > 0 };

    /// <summary>
    /// Where the schedule stands, as the engine last said (STUDIO-27, D-05): checked at startup and
    /// after each gesture, never assumed from the sidecar — that was the badge that promised a
    /// schedule nothing ran. <see cref="TeamScheduleState.Unknown"/> until the engine answered.
    /// </summary>
    public TeamScheduleState ScheduleState => _scheduleState;

    /// <summary>« Scheduled », « Not installed » or « To reinstall », as a sentence; empty while unknown.</summary>
    public string ScheduleStateLine => _scheduleState switch
    {
        TeamScheduleState.Installed => _strings[StudioStringKeys.TeamsScheduleInstalled],
        TeamScheduleState.Absent => _strings[StudioStringKeys.TeamsScheduleAbsent],
        TeamScheduleState.Stale => _strings[StudioStringKeys.TeamsScheduleStale],
        _ => "",
    };

    /// <summary>Whether the state sentence shows.</summary>
    public bool HasScheduleStateLine => _scheduleState != TeamScheduleState.Unknown;

    /// <summary>Whether the card has a schedule row: one declared, or one the engine says is registered.</summary>
    public bool HasScheduleRow =>
        IsScheduled || _scheduleState is TeamScheduleState.Installed or TeamScheduleState.Stale;

    /// <summary>« Install the schedule »: a declared schedule nothing runs, or one to reinstall.</summary>
    public bool ShowsInstallSchedule =>
        _owner.CanSchedule && IsScheduled && _scheduleState is TeamScheduleState.Absent or TeamScheduleState.Stale;

    /// <summary>« Stop the schedule »: whenever there is one to stop.</summary>
    public bool ShowsStopSchedule => _owner.CanSchedule && HasScheduleRow;

    /// <summary>« Install the schedule » — <c>forge schedule</c> on this folder.</summary>
    public AsyncRelayCommand InstallScheduleCommand { get; }

    /// <summary>« Stop the schedule » — <c>forge unschedule</c>, then the sidecar forgets it.</summary>
    public AsyncRelayCommand StopScheduleCommand { get; }

    /// <summary>What the last schedule gesture could not do; empty while nothing failed.</summary>
    public string ScheduleMessage
    {
        get => _scheduleMessage;
        private set
        {
            if (SetProperty(ref _scheduleMessage, value))
                OnPropertyChanged(nameof(HasScheduleMessage));
        }
    }

    /// <summary>Whether the failure line shows.</summary>
    public bool HasScheduleMessage => _scheduleMessage.Length > 0;

    /// <summary>The command a person runs by hand when the system refused Orkeon; null otherwise.</summary>
    public string? ScheduleManualCommand
    {
        get => _scheduleManualCommand;
        private set
        {
            if (SetProperty(ref _scheduleManualCommand, value))
                OnPropertyChanged(nameof(HasScheduleManualCommand));
        }
    }

    /// <summary>Whether the manual command shows.</summary>
    public bool HasScheduleManualCommand => _scheduleManualCommand is { Length: > 0 };

    internal void SetScheduleState(TeamScheduleState state)
    {
        if (_scheduleState == state)
            return;

        _scheduleState = state;
        OnPropertiesChanged(
            nameof(ScheduleState), nameof(ScheduleStateLine), nameof(HasScheduleStateLine), nameof(HasScheduleRow),
            nameof(ShowsInstallSchedule), nameof(ShowsStopSchedule), nameof(BadgeTone));
    }

    /// <summary>Says what a schedule gesture could not do; empty clears it.</summary>
    internal void ReportSchedule(string message, string? manualCommand)
    {
        ScheduleMessage = message;
        ScheduleManualCommand = manualCommand;
    }

    /// <summary>Hands the folder to the launcher.</summary>
    public RelayCommand LaunchCommand { get; }

    /// <summary>Copies the folder next to itself.</summary>
    public RelayCommand DuplicateCommand { get; }

    /// <summary>Arms the in-place confirmation; deletes nothing on its own.</summary>
    public RelayCommand AskDeleteCommand { get; }

    /// <summary>
    /// Deletes the team — only reachable from the armed banner: its schedule stopped first (a
    /// refusal keeps the team, and says what to run by hand), then the folder, then the workshop
    /// session when <see cref="DeleteSessionToo"/> is ticked (STUDIO-27, D-06/D-07).
    /// </summary>
    public AsyncRelayCommand ConfirmDeleteCommand { get; }

    /// <summary>Disarms the confirmation and puts the action row back.</summary>
    public RelayCommand CancelDeleteCommand { get; }

    /// <summary>Whether this card is showing its delete-this-team confirmation banner.</summary>
    public bool IsConfirmingDelete
    {
        get => _isConfirmingDelete;
        internal set
        {
            if (!SetProperty(ref _isConfirmingDelete, value))
                return;

            OnPropertyChanged(nameof(IsIdle));
            // A refusal belongs to the attempt it answered: a disarmed banner forgets it.
            if (!value)
                RefuseDelete("", null);
        }
    }

    /// <summary>
    /// Whether the banner offers « Also delete the workshop session » (STUDIO-27, D-07): only when
    /// rule R links a session to this team — a copy's id names its original's, never offered.
    /// </summary>
    public bool CanDeleteSessionToo => _linkedSession is not null;

    /// <summary>The box « Also delete the workshop session », ticked by default.</summary>
    public bool DeleteSessionToo
    {
        get => _deleteSessionToo;
        set => SetProperty(ref _deleteSessionToo, value);
    }

    /// <summary>The session rule R linked to this team when the banner was armed; null when none.</summary>
    internal ForgeSolutionSummary? LinkedSession => _linkedSession;

    /// <summary>Why the delete did not happen — its schedule could not be stopped, or the disk refused; empty otherwise.</summary>
    public string DeleteRefusal
    {
        get => _deleteRefusal;
        private set
        {
            if (SetProperty(ref _deleteRefusal, value))
                OnPropertyChanged(nameof(HasDeleteRefusal));
        }
    }

    /// <summary>Whether the banner says why the team is still there.</summary>
    public bool HasDeleteRefusal => _deleteRefusal.Length > 0;

    /// <summary>The command a person runs to stop the schedule by hand, when the system refused Orkeon.</summary>
    public string? DeleteManualCommand
    {
        get => _deleteManualCommand;
        private set
        {
            if (SetProperty(ref _deleteManualCommand, value))
                OnPropertyChanged(nameof(HasDeleteManualCommand));
        }
    }

    /// <summary>Whether the banner shows the manual command.</summary>
    public bool HasDeleteManualCommand => _deleteManualCommand is { Length: > 0 };

    /// <summary>Arms the banner's answers: the linked session, the box ticked, no refusal yet.</summary>
    internal void PrepareDelete(ForgeSolutionSummary? linkedSession)
    {
        _linkedSession = linkedSession;
        OnPropertyChanged(nameof(CanDeleteSessionToo));
        DeleteSessionToo = true;
        RefuseDelete("", null);
    }

    /// <summary>Says why the team is still there, and what to run by hand when there is something to.</summary>
    internal void RefuseDelete(string refusal, string? manualCommand)
    {
        DeleteRefusal = refusal;
        DeleteManualCommand = manualCommand;
    }

    /// <summary>The action row's own visibility — the banner or the rename editor takes its place, never sits over it.</summary>
    public bool IsIdle => !_isConfirmingDelete && !_isRenaming;

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

    /// <summary>
    /// ok / warn / accent — the badge's tone name for the view's triggers. A scheduled team is
    /// green only once the engine said the operating system runs it (STUDIO-27); not installed or
    /// to reinstall reads amber, and not asked yet claims nothing.
    /// </summary>
    public string BadgeTone => IsScheduled
        ? _scheduleState switch
        {
            TeamScheduleState.Installed => "ok",
            TeamScheduleState.Absent or TeamScheduleState.Stale => "warn",
            _ => "accent",
        }
        : _lastRun is null ? "warn" : "accent";

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

    /// <summary>
    /// The forge engine the schedule gestures go through (STUDIO-27): <c>forge schedule</c>,
    /// <c>--check</c>, <c>forge unschedule</c>. Null wires none: the cards claim no schedule
    /// state, offer no schedule action, and refuse to delete a team whose schedule they cannot stop.
    /// </summary>
    public ForgeClient? Forge { get; init; }

    /// <summary>
    /// What Studio is doing with a team folder right now (STUDIO-28, D-02; STUDIO-31, D-09):
    /// running it on the Launch or Test screen, or holding it open in the wizard. A gesture that
    /// moves or hides the folder is refused while it answers anything but
    /// <see cref="TeamActivity.None"/>. Null wires none: no team is ever busy.
    /// </summary>
    public Func<string, TeamActivity>? ActivityOf { get; init; }
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
    private readonly ForgeClient? _forge;
    private readonly Func<string, TeamActivity>? _activityOf;
    private readonly string _workspace;
    private Dictionary<string, (DateTimeOffset StartedAt, RunOutcome Outcome)> _lastRuns = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// What the engine last said of each team's schedule, by folder: a refresh rebuilds the cards
    /// from the disk, and these answers are laid back on them — a refresh never asks the engine
    /// again (STUDIO-27, D-05: at startup and after each gesture only).
    /// </summary>
    private readonly Dictionary<string, TeamScheduleState> _scheduleStates = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Builds the screen over its seams; the loaders default to the real catalogs.</summary>
    public TeamsViewModel(TeamsDependencies? dependencies = null)
    {
        var wired = dependencies ?? new TeamsDependencies();
        _declaredMounts = wired.DeclaredMounts ?? (() => []);
        _shellOpener = wired.ShellOpener;
        _historyStore = wired.HistoryStore;
        _forge = wired.Forge;
        _activityOf = wired.ActivityOf;
        var root = wired.TeamsRoot ?? TeamCatalog.DefaultRoot();
        _workspace = wired.WorkspaceDirectory ?? Environment.CurrentDirectory;
        _loadTeams = wired.LoadTeams ?? (() => TeamCatalog.List(root));
        _loadSessions = wired.LoadSessions ?? (() => ForgeSessionCatalog.List(_workspace));
        _strings = wired.Strings ?? EnglishStudioStrings.Instance;
        CreateCommand = new RelayCommand(() => CreateRequested?.Invoke(this, EventArgs.Empty));
        ImportCommand = new RelayCommand(() => ImportRequested?.Invoke(this, EventArgs.Empty));
        Refresh();
    }

    /// <summary>Raised when a team should land in the launcher.</summary>
    public event EventHandler<TeamActionEventArgs>? LaunchRequested;

    /// <summary>Raised when a stopped wizard session should resume.</summary>
    public event EventHandler<SessionResumeEventArgs>? ResumeRequested;

    /// <summary>
    /// Raised once a draft is gone from the disk — the shell tells the wizard, which forgets
    /// the session when it is the one it was open on. Not raised for a delete the disk refused.
    /// </summary>
    public event EventHandler<SessionDeletedEventArgs>? SessionDeleted;

    /// <summary>Raised by the create-a-team button — the shell brings the wizard forward.</summary>
    public event EventHandler? CreateRequested;

    /// <summary>
    /// Raised by the empty state's second way out — the shell brings the import screen
    /// forward. An empty My-teams offers both doors, not just the one.
    /// </summary>
    public event EventHandler? ImportRequested;

    /// <summary>Raised by "Change the folders" — the shell opens the team-mounts modal.</summary>
    public event EventHandler<TeamMountsRequestedEventArgs>? MountsRequested;

    /// <summary>Raised by the card's Tester icon — the shell brings the trial screen forward.</summary>
    public event EventHandler<TeamActionEventArgs>? TestRequested;

    /// <summary>Raised by «Modifier» — the shell reopens the wizard on the team (W-09).</summary>
    public event EventHandler<TeamModifyEventArgs>? ModifyRequested;

    /// <summary>
    /// Raised once a team is renamed (STUDIO-28): the shell reloads the history the Run screen
    /// lists (D-04) and lets a launcher aimed at the former folder follow the team.
    /// </summary>
    public event EventHandler<TeamRenamedEventArgs>? TeamRenamed;

    /// <summary>
    /// Whether «Modifier» can reopen the wizard on <paramref name="team"/> (STUDIO-25, D-04): its
    /// <c>forge.json</c> names a session, or its crew can be read back into one. Which session,
    /// if any, is linked is never decided here — <c>forge reopen</c> applies rule R.
    /// </summary>
    internal static bool CanReopen(TeamSummary team) => team.ForgeSessionId is not null || team.HasYamlCrew;

    internal void RequestModify(TeamSummary team)
    {
        if (CanReopen(team))
            ModifyRequested?.Invoke(this, new TeamModifyEventArgs(team));
    }

    /// <summary>
    /// What Studio is doing with the team at <paramref name="teamPath"/> (STUDIO-28, D-02): asked
    /// at the moment of a gesture that moves or hides its folder, never cached — a run starts and
    /// ends between two clicks. <see cref="TeamActivity.None"/> when no hook is wired.
    /// </summary>
    internal TeamActivity ActivityOf(string teamPath) => _activityOf?.Invoke(teamPath) ?? TeamActivity.None;

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
        ApplyScheduleStates();
        OnPropertiesChanged(nameof(Count), nameof(IsEmpty), nameof(HasInProgress));
    }

    /// <summary>Whether the schedule gestures can reach the engine at all.</summary>
    public bool CanSchedule => _forge is not null;

    /// <summary>Whether « Rename » can reach the engine — the folder, its session and its schedule are the engine's to move (STUDIO-28).</summary>
    public bool CanRename => _forge is not null;

    /// <summary>
    /// Asks the engine where every team's schedule stands (STUDIO-27, D-05) — the shell runs this
    /// once at startup. Only teams that have one are asked, one at a time.
    /// </summary>
    public async Task CheckSchedulesAsync(CancellationToken cancellationToken = default)
    {
        if (_forge is null)
            return;

        foreach (var path in Teams.Where(card => card.Summary.HasSchedule).Select(card => card.Summary.Path).ToList())
            await CheckScheduleAsync(path, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Asks the engine where one team's schedule stands — after a gesture that may have changed it:
    /// an adoption, an import, a duplication. A team with no schedule any more is forgotten without
    /// asking; an answer the engine could not give is recorded as unknown, never as a guess.
    /// </summary>
    [SuppressMessage("Design", "CA1031",
        Justification = "A check is comfort, not truth: whatever it could not learn leaves the card saying nothing.")]
    public async Task CheckScheduleAsync(string teamPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamPath);
        if (_forge is null)
            return;

        if (!TeamCatalog.Describe(teamPath).HasSchedule)
        {
            RecordScheduleState(teamPath, TeamScheduleState.Unknown);
            return;
        }

        TeamScheduleState state;
        try
        {
            var report = await _forge.ScheduleAsync(teamPath, ForgeScheduleVerb.Check, cancellationToken).ConfigureAwait(true);
            state = report.Succeeded ? report.State : TeamScheduleState.Unknown;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            state = TeamScheduleState.Unknown;
        }

        RecordScheduleState(teamPath, state);
    }

    /// <summary>Records what the engine said of <paramref name="teamPath"/>'s schedule, and shows it on its card.</summary>
    public void RecordScheduleState(string teamPath, TeamScheduleState state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamPath);

        var key = NormalizePath(teamPath);
        if (state == TeamScheduleState.Unknown)
            _scheduleStates.Remove(key);
        else
            _scheduleStates[key] = state;

        ApplyScheduleStates();
    }

    private void ApplyScheduleStates()
    {
        foreach (var card in Teams)
        {
            card.SetScheduleState(_scheduleStates.TryGetValue(NormalizePath(card.Summary.Path), out var state)
                ? state
                : TeamScheduleState.Unknown);
        }
    }

    /// <summary>« Install the schedule » (D-05): <c>forge schedule</c>, the card then shows what the engine answered.</summary>
    internal async Task InstallScheduleAsync(TeamCardViewModel card)
    {
        card.ReportSchedule("", null);
        var report = await ScheduleAsync(card.Summary.Path, ForgeScheduleVerb.Install).ConfigureAwait(true);
        if (!report.Succeeded)
        {
            card.ReportSchedule(
                string.Format(CultureInfo.CurrentCulture, _strings[StudioStringKeys.TeamsScheduleInstallFailed], report.FailureReason),
                report.ManualCommand);
            return;
        }

        RecordScheduleState(card.Summary.Path, report.State);
    }

    /// <summary>
    /// « Stop the schedule » (D-05): <c>forge unschedule</c>, then the sidecar forgets the schedule —
    /// the card is on demand from then on. A refusal changes nothing, and says what to run by hand.
    /// </summary>
    internal async Task StopScheduleAsync(TeamCardViewModel card)
    {
        card.ReportSchedule("", null);
        var report = await ScheduleAsync(card.Summary.Path, ForgeScheduleVerb.Remove).ConfigureAwait(true);
        if (!report.Succeeded)
        {
            card.ReportSchedule(
                string.Format(CultureInfo.CurrentCulture, _strings[StudioStringKeys.TeamsScheduleStopFailed], report.FailureReason),
                report.ManualCommand);
            return;
        }

        TeamCatalog.ClearSchedule(card.Summary.Path);
        _scheduleStates.Remove(NormalizePath(card.Summary.Path));
        Refresh();
    }

    /// <summary>One schedule verb; a screen wired without an engine gets a run that never started.</summary>
    [SuppressMessage("Design", "CA1031",
        Justification = "A launch fault is the refusal the card says, never an exception in a discarded task.")]
    private async Task<ForgeScheduleReport> ScheduleAsync(string teamPath, ForgeScheduleVerb verb)
    {
        if (_forge is null)
            return new ForgeScheduleReport { Run = ProcessRunResult.NotStarted("No forge engine is wired to this screen.") };

        try
        {
            return await _forge.ScheduleAsync(teamPath, verb).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ForgeScheduleReport { Run = ProcessRunResult.NotStarted(ex.Message) };
        }
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
        if (TeamCatalog.Duplicate(path) is not { } copy)
            return;

        Refresh();
        // A copy carries its original's schedule, which runs the original: what runs the copy is
        // the engine's to say (STUDIO-27).
        _ = CheckScheduleAsync(copy);
    }

    /// <summary>
    /// Opens <paramref name="card"/>'s rename editor on its current name (STUDIO-28, D-07), and
    /// closes every other question on screen — another editor, a delete banner: one answer at a time.
    /// </summary>
    internal void BeginRename(TeamCardViewModel card)
    {
        foreach (var other in Teams)
        {
            other.IsConfirmingDelete = false;
            other.IsRenaming = ReferenceEquals(other, card);
        }

        foreach (var session in InProgress)
            session.IsConfirmingDelete = false;

        card.RenameText = card.Name;
    }

    /// <summary>
    /// « Rename » (STUDIO-28). Refused, on the card, while Studio runs the team, tests it or has it
    /// open in the wizard (D-02), and when the new name's folder is taken (D-03). Otherwise the
    /// engine renames the folder, the linked session, the titles, the generated files and the
    /// schedule — all of it or nothing (D-01). Then what is Studio's own follows: the launch
    /// history (D-04) and what the engine said of the schedule; an allowed folder the settings
    /// declare inside the former folder is said, never rewritten (D-05).
    /// </summary>
    internal async Task RenameAsync(TeamCardViewModel card)
    {
        var team = card.Summary;
        card.RefuseRename("");
        if (!TeamCatalog.TryNormalizeName(card.RenameText, out var name))
            return;

        // The name the team already has: nothing to rename.
        if (string.Equals(name, card.Name, StringComparison.Ordinal))
        {
            card.IsRenaming = false;
            return;
        }

        if (BusyRefusal(team.Path) is { } busy)
        {
            card.RefuseRename(busy);
            return;
        }

        var folder = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(System.IO.Path.TrimEndingDirectorySeparator(team.Path)) ?? team.Path,
            FolderSlug.From(name) ?? FolderSlug.TeamFallback);
        if (!string.Equals(NormalizePath(folder), NormalizePath(team.Path), PhysicalPathContainment.Comparison)
            && TakenRefusal(folder) is { } taken)
        {
            card.RefuseRename(taken);
            return;
        }

        var report = await RenameThroughEngineAsync(team.Path, name).ConfigureAwait(true);
        if (!report.Succeeded)
        {
            card.RefuseRename(string.Format(CultureInfo.CurrentCulture, _strings[StudioStringKeys.TeamsRenameFailed], report.FailureReason));
            return;
        }

        // A name whose folder is the team's own only retitled it: nothing Studio keeps moved.
        var renamed = report.Path!;
        if (!string.Equals(NormalizePath(renamed), NormalizePath(team.Path), PhysicalPathContainment.Comparison))
        {
            await RebaseHistoryAsync(team.Path, renamed).ConfigureAwait(true);
            FollowScheduleState(team, renamed, report.ScheduleState);
        }

        StatusMessage = RenamedLine(name, team.Path, renamed, report.Warnings);
        Refresh();
        TeamRenamed?.Invoke(this, new TeamRenamedEventArgs(team.Path, renamed));
    }

    /// <summary>Why <paramref name="teamPath"/> cannot move right now (D-02); null when nothing holds it.</summary>
    private string? BusyRefusal(string teamPath) => ActivityOf(teamPath) switch
    {
        TeamActivity.Running => _strings[StudioStringKeys.TeamsRenameBusyRunning],
        TeamActivity.Testing => _strings[StudioStringKeys.TeamsRenameBusyTesting],
        TeamActivity.OpenInWizard => _strings[StudioStringKeys.TeamsRenameBusyWizard],
        _ => null,
    };

    /// <summary>
    /// What occupies <paramref name="folder"/>, in the words the wizard uses for the same collision
    /// (STUDIO-26): a team, by its name; a folder that holds none; a file. Null when it is free.
    /// </summary>
    private string? TakenRefusal(string folder)
    {
        var name = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(folder));
        return TeamCatalog.OccupantOf(folder) switch
        {
            TeamFolderOccupant.None => null,
            TeamFolderOccupant.Team => string.Format(
                CultureInfo.CurrentCulture, _strings[StudioStringKeys.WizardNameTakenTeam],
                TeamCatalog.NormalizeName(TeamCatalog.Describe(folder).Name), name),
            TeamFolderOccupant.Folder => string.Format(CultureInfo.CurrentCulture, _strings[StudioStringKeys.WizardNameTakenFolder], name),
            _ => string.Format(CultureInfo.CurrentCulture, _strings[StudioStringKeys.WizardNameTakenFile], name),
        };
    }

    /// <summary>The engine's rename, in the workshop's workspace; a screen wired without an engine gets a run that never started.</summary>
    [SuppressMessage("Design", "CA1031",
        Justification = "A launch fault is the refusal the card says, never an exception in a discarded task.")]
    private async Task<ForgeRenameReport> RenameThroughEngineAsync(string teamPath, string name)
    {
        if (_forge is null)
            return new ForgeRenameReport { Run = ProcessRunResult.NotStarted("No forge engine is wired to this screen.") };

        try
        {
            return await _forge.RenameAsync(teamPath, name, _workspace).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ForgeRenameReport { Run = ProcessRunResult.NotStarted(ex.Message) };
        }
    }

    /// <summary>
    /// D-04: the launches of the former folder are spelled under the new one, so the card keeps its
    /// last run and « Relaunch » replays it where the team is. History is comfort: a store that
    /// refuses costs the line, never the rename.
    /// </summary>
    private async Task RebaseHistoryAsync(string from, string to)
    {
        if (_historyStore is null)
            return;

        try
        {
            var history = await _historyStore.LoadAsync().ConfigureAwait(true);
            await _historyStore.SaveAsync(history.Rebase(from, to)).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            // The card forgets its last run; the team itself is renamed.
        }

        await LoadLastRunsAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// The schedule's state moves with the folder: what the engine said of the reinstalled schedule,
    /// else a question to the engine for a team that has one — the former folder's answer spoke of
    /// the former folder.
    /// </summary>
    private void FollowScheduleState(TeamSummary team, string renamed, TeamScheduleState reinstalled)
    {
        _scheduleStates.Remove(NormalizePath(team.Path));
        if (reinstalled != TeamScheduleState.Unknown)
            _scheduleStates[NormalizePath(renamed)] = reinstalled;
        else if (team.HasSchedule)
            _ = CheckScheduleAsync(renamed);
    }

    /// <summary>
    /// The screen's line once renamed: the name and the folder — then, D-05, the folders the settings
    /// allow inside the former folder, which pointed into the team and now point nowhere. The user's
    /// settings are said, never rewritten. The engine's own warnings follow, in its words.
    /// </summary>
    private string RenamedLine(string name, string from, string renamed, IReadOnlyList<string> warnings)
    {
        var parts = new List<string>
        {
            string.Format(
                CultureInfo.CurrentCulture, _strings[StudioStringKeys.TeamsRenamed],
                name, System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(renamed))),
        };

        var stranded = _declaredMounts()
            .Where(entry => !TeamMountPaths.IsTeamRelative(entry) && DeclaredMounts.IsInsideTeam(entry, from))
            .Select(entry => MountDefinition.TryParse(entry, out var mount, out _) && mount is not null ? mount.PhysicalPath : entry)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (stranded.Count > 0)
        {
            parts.Add(string.Format(
                CultureInfo.CurrentCulture, _strings[StudioStringKeys.TeamsRenameStrandedFolders], string.Join(", ", stranded)));
        }

        parts.AddRange(warnings);
        return string.Join(" ", parts);
    }

    /// <summary>
    /// Deletes a team, and leaves nothing behind (STUDIO-27, D-06/D-07). Rule R names the linked
    /// session first — it reads this folder, and the copy test reads the original's. Then a
    /// schedule, declared or recorded as installed, is stopped: a refusal keeps the team, says
    /// why and what to run by hand. Then the folder; then, box ticked, the session — never the
    /// original's for a copy, which rule R links to no session.
    /// </summary>
    internal async Task DeleteAsync(TeamCardViewModel card)
    {
        var team = card.Summary;
        // Only a session the banner offered: the box is ticked by default, but never for a session
        // the user was not shown.
        var session = card.CanDeleteSessionToo && card.DeleteSessionToo ? LinkedSessionOf(team) : null;

        if (team.HasSchedule || card.ScheduleState is TeamScheduleState.Installed or TeamScheduleState.Stale)
        {
            var report = await ScheduleAsync(team.Path, ForgeScheduleVerb.Remove).ConfigureAwait(true);
            if (!report.Succeeded)
            {
                card.RefuseDelete(
                    string.Format(CultureInfo.CurrentCulture, _strings[StudioStringKeys.TeamsDeleteUnscheduleFailed], report.FailureReason),
                    report.ManualCommand);
                return;
            }
        }

        if (!TeamCatalog.Delete(team.Path))
        {
            card.RefuseDelete(_strings[StudioStringKeys.TeamsDeleteRefused], null);
            return;
        }

        _scheduleStates.Remove(NormalizePath(team.Path));
        if (session is not null && ForgeSessionCatalog.Delete(session.Directory))
            SessionDeleted?.Invoke(this, new SessionDeletedEventArgs(session));

        Refresh();
    }

    /// <summary>The session rule R links <paramref name="team"/> to, among the sessions this screen lists; null for a copy or none.</summary>
    private ForgeSolutionSummary? LinkedSessionOf(TeamSummary team) =>
        team.ForgeSessionId is null ? null : ForgeSessionCatalog.LinkedSession(_loadSessions(), team.Path);

    /// <summary>
    /// Discards an abandoned wizard draft. The session directory is the whole of it —
    /// a draft that never promoted owns nothing else. The wizard may be open on that very
    /// session (« Resume » brought it there a moment ago): the delete is announced so the
    /// shell can have it forgotten there too, before the list re-reads the disk.
    /// </summary>
    internal void DeleteSession(ForgeSolutionSummary session)
    {
        if (!ForgeSessionCatalog.Delete(session.Directory))
            return;

        SessionDeleted?.Invoke(this, new SessionDeletedEventArgs(session));
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
        {
            var armed = ReferenceEquals(card, row);
            // The banner's box needs the session before it opens (STUDIO-27, D-07).
            if (armed)
                card.PrepareDelete(LinkedSessionOf(card.Summary));
            // One question on screen at a time: a rename editor closes too (STUDIO-28).
            card.IsRenaming = false;
            card.IsConfirmingDelete = armed;
        }

        foreach (var session in InProgress)
            session.IsConfirmingDelete = ReferenceEquals(session, row);
    }
}
