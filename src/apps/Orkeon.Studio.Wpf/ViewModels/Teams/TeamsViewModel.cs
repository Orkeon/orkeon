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
using Orkeon.Studio.Wpf.ViewModels.Common;
using Orkeon.Studio.Wpf.ViewModels.Mounts;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;
using Orkeon.Studio.Wpf.ViewModels.Services;

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

/// <summary>
/// Payload of an archive or a restore (STUDIO-31, STUDIO-32): the team folders one gesture archived
/// or restored — one, or every team an accepted suggestion or its undo moved. One gesture, one
/// signal: the shell re-reads the other lists once, not once per team.
/// </summary>
public sealed class ArchiveChangedEventArgs(IReadOnlyList<string> paths) : EventArgs
{
    /// <summary>The team folders whose archived state changed.</summary>
    [SuppressMessage("Minor Code Smell", "S3604:Member initializer values should not be redundant",
        Justification = "False positive on a primary constructor: the initializer IS the only "
                      + "assignment of the member, and removing it would leave it unset.")]
    public IReadOnlyList<string> Paths { get; } = paths;
}

/// <summary>How My teams orders its cards (STUDIO-32, D-01).</summary>
public enum TeamSortOrder
{
    /// <summary>The most recent activity first (STUDIO-31, D-05) — the default; a team whose activity is unknown comes last.</summary>
    LastActivity,

    /// <summary>By name.</summary>
    Name,
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
    private string _archiveNotice = "";
    private string? _archiveManualCommand;
    private bool _offersScheduleStop;
    private bool _offersRestore;
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
        // STUDIO-32 (D-02): an archived card offers « Restore » and « Delete », nothing else — it is
        // neither launched, modified, renamed nor copied, and its folders are not changed.
        LaunchCommand = new RelayCommand(() => owner.RequestLaunch(summary.Path), () => !IsArchived);
        DuplicateCommand = new RelayCommand(() => owner.Duplicate(summary.Path), () => !IsArchived);
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
        OpenCommand = new RelayCommand(() => owner.OpenInShell(summary.Path), () => owner.CanOpenInShell && !IsArchived);
        ChangeMountsCommand = new RelayCommand(() => owner.RequestMounts(this), () => !IsArchived);
        ExportCommand = new RelayCommand(() => owner.Export(summary.Path), () => !IsArchived);
        // STUDIO-31 (D-07): an archived team is not tested from Studio — the icon offers the restore.
        TestCommand = new RelayCommand(() => owner.RequestTest(this));
        // STUDIO-31: the model's gestures; STUDIO-32 puts them on screen — « Archive » without a
        // question, then the undo banner. A refusal or an offer lands on the card (ArchiveNotice),
        // never in silence.
        ArchiveCommand = new AsyncRelayCommand(() => owner.ArchiveAsync(this, stopSchedule: false), () => !IsArchived);
        StopScheduleAndArchiveCommand = new AsyncRelayCommand(() => owner.ArchiveAsync(this, stopSchedule: true), () => !IsArchived);
        RestoreCommand = new RelayCommand(() => owner.Restore(this), () => IsArchived);
        DismissArchiveNoticeCommand = new RelayCommand(ClearArchiveNotice);
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
        ModifyCommand = new RelayCommand(() => owner.RequestModify(summary), () => CanModify && !IsArchived);
        ToggleDescriptionCommand = new RelayCommand(() => IsDescriptionExpanded = !IsDescriptionExpanded);
        // STUDIO-28 (D-07): « Rename » opens an editor in place of the action row, like the delete
        // banner — no MessageBox. The engine renames the folder and everything that follows it.
        RenameCommand = new RelayCommand(() => owner.BeginRename(this), () => owner.CanRename && !IsArchived);
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
            _owner.OnQuestionChanged();
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

    /// <summary>« Install the schedule »: a declared schedule nothing runs, or one to reinstall — never on an archived team.</summary>
    public bool ShowsInstallSchedule =>
        _owner.CanSchedule && IsScheduled && !IsArchived && _scheduleState is TeamScheduleState.Absent or TeamScheduleState.Stale;

    /// <summary>
    /// Whether the team has a schedule to stop before it is archived or deleted (STUDIO-27, D-06;
    /// STUDIO-31, D-06): declared, recorded as installed, or said to be by the engine. The system runs
    /// such a team where Studio cannot see it — which is also why the archive suggestion never
    /// proposes one (STUDIO-32, DB-1).
    /// </summary>
    internal bool HoldsSchedule =>
        Summary.HasSchedule || _scheduleState is TeamScheduleState.Installed or TeamScheduleState.Stale;

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
            _owner.OnQuestionChanged();
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

    /// <summary>Whether the team is archived (STUDIO-31, D-01): out of the active list, its folder untouched.</summary>
    public bool IsArchived => Summary.IsArchived;

    /// <summary>
    /// « Archive » — refused while the team is busy (D-09) or scheduled (D-06); the notice then
    /// says why, or offers « Stop the schedule and archive ».
    /// </summary>
    public AsyncRelayCommand ArchiveCommand { get; }

    /// <summary>« Stop the schedule and archive » (D-06): <c>forge unschedule</c> first, then the archive.</summary>
    public AsyncRelayCommand StopScheduleAndArchiveCommand { get; }

    /// <summary>« Restore » — the team takes its place again in the active list (D-10); refused while it is busy (D-09).</summary>
    public RelayCommand RestoreCommand { get; }

    /// <summary>Closes the notice.</summary>
    public RelayCommand DismissArchiveNoticeCommand { get; }

    /// <summary>What the last archive gesture has to say — a refusal or an offer; empty while quiet.</summary>
    public string ArchiveNotice
    {
        get => _archiveNotice;
        private set
        {
            if (!SetProperty(ref _archiveNotice, value))
                return;

            OnPropertyChanged(nameof(HasArchiveNotice));
            _owner.OnQuestionChanged();
        }
    }

    /// <summary>Whether the card shows its archive notice.</summary>
    public bool HasArchiveNotice => _archiveNotice.Length > 0;

    /// <summary>Whether the notice offers « Stop the schedule and archive » (D-06).</summary>
    public bool OffersScheduleStop
    {
        get => _offersScheduleStop;
        private set => SetProperty(ref _offersScheduleStop, value);
    }

    /// <summary>Whether the notice offers « Restore »: the Test icon met an archived team (D-07).</summary>
    public bool OffersRestore
    {
        get => _offersRestore;
        private set => SetProperty(ref _offersRestore, value);
    }

    /// <summary>The command a person runs to stop the schedule by hand, when the system refused Orkeon.</summary>
    public string? ArchiveManualCommand
    {
        get => _archiveManualCommand;
        private set
        {
            if (SetProperty(ref _archiveManualCommand, value))
                OnPropertyChanged(nameof(HasArchiveManualCommand));
        }
    }

    /// <summary>Whether the notice shows the manual command.</summary>
    public bool HasArchiveManualCommand => _archiveManualCommand is { Length: > 0 };

    /// <summary>Says why the gesture did not happen, and what to run by hand when there is something to.</summary>
    internal void ReportArchive(string notice, string? manualCommand = null)
    {
        OffersScheduleStop = false;
        OffersRestore = false;
        ArchiveNotice = notice;
        ArchiveManualCommand = manualCommand;
    }

    /// <summary>Offers « Stop the schedule and archive » under <paramref name="notice"/> (D-06).</summary>
    internal void OfferScheduleStop(string notice)
    {
        ReportArchive(notice);
        OffersScheduleStop = true;
    }

    /// <summary>Offers « Restore » under <paramref name="notice"/> (D-07).</summary>
    internal void OfferRestore(string notice)
    {
        ReportArchive(notice);
        OffersRestore = true;
    }

    /// <summary>Forgets the notice and its offer.</summary>
    internal void ClearArchiveNotice() => ReportArchive("");

    /// <summary>The team's mount strings, straight from the sidecar.</summary>
    public IReadOnlyList<string> Mounts => Summary.Mounts;

    /// <summary>Whether any mount is recorded — the card's "Dossiers :" line.</summary>
    public bool HasMounts => Summary.Mounts.Count > 0;

    /// <summary>Agent definitions counted on disk; null when the folder shows none.</summary>
    public int? AgentCount => Summary.AgentCount;

    /// <summary>
    /// When this team last ran from Studio: its latest launch-history entry, or the date its sidecar
    /// recorded (STUDIO-31, D-05) — the history keeps fifty runs and the team outlives them, and the
    /// card must not say « never ran » of a team the order and the archive suggestion read as run
    /// (STUDIO-32). Null when it never did.
    /// </summary>
    public DateTimeOffset? LastRun => new[] { _lastRun, Summary.LastRunAt }.Max();

    /// <summary>How that last run ended, while the history still has it; null when the team never ran, or when only its sidecar remembers the run.</summary>
    public RunOutcome? LastOutcome => _lastRun is { } recorded && recorded >= (Summary.LastRunAt ?? recorded) ? _lastOutcome : null;

    /// <summary>
    /// The team's last activity (STUDIO-31, D-05) — the most recent of the last run its sidecar
    /// records, its latest launch-history entry, its promotion and a copy's arrival; null when none is
    /// known. What the screen sorts on and what the archive suggestion reads (STUDIO-32).
    /// </summary>
    public DateTimeOffset? LastActivity => Summary.LastActivity(_lastRun);

    /// <summary>The mount chips, virtual path + rights in words.</summary>
    public IReadOnlyList<TeamMountChip> MountChips { get; }

    /// <summary>
    /// The card badge (mock: scheduled = green, to be tested = amber, on demand = accent).
    /// A team that never ran and is not scheduled still has to earn its first run.
    /// </summary>
    public string BadgeText
    {
        get
        {
            if (IsArchived)
                return _strings[StudioStringKeys.TeamsArchivedBadge];

            return IsScheduled || LastRun is not null ? ScheduleDisplay : _strings[StudioStringKeys.TeamsToTest];
        }
    }

    /// <summary>
    /// ok / warn / accent / muted — the badge's tone name for the view's triggers. A scheduled team
    /// is green only once the engine said the operating system runs it (STUDIO-27); not installed or
    /// to reinstall reads amber, and not asked yet claims nothing. An archived team is muted
    /// (STUDIO-32): out of the list, nothing about it is urgent.
    /// </summary>
    public string BadgeTone
    {
        get
        {
            if (IsArchived)
                return "muted";

            if (!IsScheduled)
                return LastRun is null ? "warn" : "accent";

            return _scheduleState switch
            {
                TeamScheduleState.Installed => "ok",
                TeamScheduleState.Absent or TeamScheduleState.Stale => "warn",
                _ => "accent",
            };
        }
    }

    /// <summary>The last-run date and how it ended — the date alone when only the sidecar remembers it — or never-ran: the meta line's history part.</summary>
    public string LastRunDisplay
    {
        get
        {
            if (LastRun is not { } startedAt)
                return _strings[StudioStringKeys.TeamsNeverRan];

            var date = startedAt.ToLocalTime().ToString("d", CultureInfo.CurrentCulture);
            if (LastOutcome is not { } outcome)
                return string.Format(CultureInfo.CurrentCulture, _strings[StudioStringKeys.TeamsLastRunDate], date);

            return string.Format(
                CultureInfo.CurrentCulture, _strings[StudioStringKeys.TeamsLastRun], date,
                _strings[outcome == RunOutcome.Success ? StudioStringKeys.TeamsRunOk : StudioStringKeys.TeamsRunFail]);
        }
    }

    /// <summary>« Archived on 24/09/2026 » — an archived card's meta part, in place of its schedule (STUDIO-32).</summary>
    public string ArchivedDisplay => Summary.ArchivedAt is { } archivedAt
        ? string.Format(
            CultureInfo.CurrentCulture, _strings[StudioStringKeys.TeamsArchivedOn],
            archivedAt.ToLocalTime().ToString("d", CultureInfo.CurrentCulture))
        : _strings[StudioStringKeys.TeamsArchivedBadge];

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
        new[] { AgentCountDisplay, IsArchived ? ArchivedDisplay : ScheduleDisplay, LastRunDisplay, ProfileDisplay }
            .Where(part => !string.IsNullOrWhiteSpace(part)));

    /// <summary>What the search reads (STUDIO-32, D-01): the name and the whole need, as a reader of the card sees them.</summary>
    internal string SearchableText => $"{Name} {Description}";

    internal void SetLastRun(DateTimeOffset? startedAt, RunOutcome? outcome)
    {
        if (_lastRun == startedAt && _lastOutcome == outcome)
            return;

        _lastRun = startedAt;
        _lastOutcome = outcome;
        OnPropertiesChanged(
            nameof(LastRun), nameof(LastOutcome), nameof(LastRunDisplay),
            nameof(BadgeText), nameof(BadgeTone), nameof(MetaLine), nameof(LastActivity));
    }
}

/// <summary>One resumable wizard session, listed under the teams.</summary>
public sealed class InProgressSessionViewModel : ObservableObject
{
    private readonly TeamsViewModel _owner;
    private bool _isConfirmingDelete;

    internal InProgressSessionViewModel(ForgeSolutionSummary summary, TeamsViewModel owner)
    {
        _owner = owner;
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
            if (!SetProperty(ref _isConfirmingDelete, value))
                return;

            OnPropertyChanged(nameof(IsIdle));
            _owner.OnQuestionChanged();
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

    /// <summary>The clock an archive, a copy's arrival and the archive suggestion are read on (STUDIO-31, STUDIO-32); the system's when null.</summary>
    public TimeProvider? Clock { get; init; }

    /// <summary>
    /// Settings › Studio as in force now — the archive suggestion's switch and threshold (STUDIO-32,
    /// DB-1), read each time the suggestion is worked out; the defaults when null.
    /// </summary>
    public Func<StudioSettings>? StudioSettings { get; init; }

    /// <summary>
    /// The undo banner's timer (STUDIO-32, D-02) — an instance of its own: the banner drops its pause
    /// with <c>CancelPending</c>, which on the instance the chat uses would drop the assistant's
    /// beats too. Immediate when null: the banner then goes as soon as it came.
    /// </summary>
    public IUiDelay? UndoDelay { get; init; }
}

/// <summary>
/// The my-teams screen (design v3): every adopted team is an ordinary folder under the teams
/// root — copiable, deletable, runnable with <c>orkeon run</c> alone — plus the wizard
/// sessions still underway, resumable where they stopped.
/// <para>
/// STUDIO-32 keeps it readable with many teams: a search and an order applied to the cards in
/// memory (D-01), the Archives view, « Archive » followed by an undo banner (D-02), and the
/// proposal to archive the teams nobody launches any more (DB-1).
/// </para>
/// </summary>
public sealed class TeamsViewModel : ObservableObject
{
    /// <summary>How long the undo banner stays (D-02): a few seconds, long enough to read one line and click.</summary>
    private static readonly TimeSpan UndoWindow = TimeSpan.FromSeconds(8);

    private readonly Func<IReadOnlyList<TeamSummary>> _loadTeams;
    private readonly Func<IReadOnlyList<string>> _declaredMounts;
    private readonly IShellOpener? _shellOpener;
    private readonly Func<IReadOnlyList<ForgeSolutionSummary>> _loadSessions;
    private readonly IStudioStrings _strings;
    private readonly ILaunchHistoryStore? _historyStore;
    private readonly ForgeClient? _forge;
    private readonly Func<string, TeamActivity>? _activityOf;
    private readonly TimeProvider _clock;
    private readonly Func<StudioSettings> _studioSettings;
    private readonly IUiDelay _undoDelay;
    private readonly string _workspace;
    private Dictionary<string, (DateTimeOffset StartedAt, RunOutcome Outcome)> _lastRuns = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether the launch history was read, or there is none to read: until then a team's last
    /// activity may be older than it is, and nothing is proposed for archiving on it (DB-1).
    /// </summary>
    private bool _lastRunsKnown;

    private string _searchText = "";
    private TeamSortOrder _sortOrder = TeamSortOrder.LastActivity;
    private bool _showArchives;

    /// <summary>What the undo banner would put back (D-02); null while it is not shown.</summary>
    private IReadOnlyList<ArchivedTeam>? _undo;

    /// <summary>The teams the archive suggestion lists (DB-1), oldest activity first; empty when it has none.</summary>
    private IReadOnlyList<TeamCardViewModel> _suggested = [];

    /// <summary>The threshold the suggestion was worked out with, in days — what its sentence says.</summary>
    private int _suggestionDays = StudioSettings.DefaultArchiveSuggestionDays;

    /// <summary>Whether the suggestion was answered — accepted or dismissed: it is not made again this session.</summary>
    private bool _suggestionAnswered;

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
        _clock = wired.Clock ?? TimeProvider.System;
        _studioSettings = wired.StudioSettings ?? (() => StudioSettings.Default);
        _undoDelay = wired.UndoDelay ?? ImmediateUiDelay.Instance;
        _lastRunsKnown = _historyStore is null;
        var root = wired.TeamsRoot ?? TeamCatalog.DefaultRoot();
        _workspace = wired.WorkspaceDirectory ?? Environment.CurrentDirectory;
        // Every team, archived or not: the screen splits them (STUDIO-31).
        _loadTeams = wired.LoadTeams ?? (() => TeamCatalog.List(root, TeamListFilter.All));
        _loadSessions = wired.LoadSessions ?? (() => ForgeSessionCatalog.List(_workspace));
        _strings = wired.Strings ?? EnglishStudioStrings.Instance;
        CreateCommand = new RelayCommand(() => CreateRequested?.Invoke(this, EventArgs.Empty));
        ImportCommand = new RelayCommand(() => ImportRequested?.Invoke(this, EventArgs.Empty));
        ClearSearchCommand = new RelayCommand(() => SearchText = "");
        SortByActivityCommand = new RelayCommand(() => SortOrder = TeamSortOrder.LastActivity);
        SortByNameCommand = new RelayCommand(() => SortOrder = TeamSortOrder.Name);
        ToggleArchivesCommand = new RelayCommand(() => ShowArchives = !ShowArchives);
        UndoCommand = new RelayCommand(Undo, () => _undo is not null);
        DismissUndoCommand = new RelayCommand(DropUndo);
        ArchiveSuggestedCommand = new RelayCommand(ArchiveSuggested, () => _suggested.Count > 0);
        DismissSuggestionCommand = new RelayCommand(DismissSuggestion);
        // The screen's own sentences follow the language; the cards' are rebuilt on each refresh.
        _strings.CultureChanged += (_, _) => OnPropertiesChanged(
            nameof(ArchivesLabel), nameof(NoMatchLine), nameof(UndoNotice), nameof(ArchiveSuggestionLine));
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
    /// Raised once teams were archived or restored (STUDIO-31, D-08) — the shell re-reads what lists
    /// the active teams elsewhere: the Test picker, and the two launchers' view of their target. Once
    /// per gesture, however many teams it moved (STUDIO-32).
    /// </summary>
    public event EventHandler<ArchiveChangedEventArgs>? ArchiveChanged;

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

    /// <summary>
    /// The cards of the active teams (STUDIO-31), in the catalogue's order: what the screen lists
    /// from, and what the shell listens to — each <see cref="Refresh"/> resets it, once.
    /// </summary>
    public ObservableCollection<TeamCardViewModel> Teams { get; } = [];

    /// <summary>The cards of the archived teams (STUDIO-31): out of the active list, restorable from the Archives view.</summary>
    public ObservableCollection<TeamCardViewModel> ArchivedTeams { get; } = [];

    /// <summary>The wizard sessions still underway.</summary>
    public ObservableCollection<InProgressSessionViewModel> InProgress { get; } = [];

    /// <summary>
    /// What the screen lists (STUDIO-32, D-01): the active cards, or the archived ones in the
    /// Archives view, those the search finds, in the chosen order. A view over the cards in memory —
    /// the search, the order and the view never read the disk again, nor reset <see cref="Teams"/>.
    /// </summary>
    public IReadOnlyList<TeamCardViewModel> Cards { get; private set; } = [];

    /// <summary>
    /// Number of active teams — the sidebar's count (D-03): the teams in use, whatever the search
    /// finds and whichever view shows.
    /// </summary>
    public int ActiveCount => Teams.Count;

    /// <summary>Number of archived teams.</summary>
    public int ArchivedCount => ArchivedTeams.Count;

    /// <summary>Whether any team is archived — the « Archives » toggle shows only then.</summary>
    public bool HasArchives => ArchivedTeams.Count > 0;

    /// <summary>« Archives (3) » — the toggle's label.</summary>
    public string ArchivesLabel =>
        string.Format(CultureInfo.CurrentCulture, _strings[StudioStringKeys.TeamsArchivesLabel], ArchivedTeams.Count);

    /// <summary>Every card, active then archived: what the schedule answers and the last runs are laid on.</summary>
    private IEnumerable<TeamCardViewModel> AllCards => Teams.Concat(ArchivedTeams);

    /// <summary>Whether the cards can offer "Ouvrir" at all (a shell opener was wired).</summary>
    public bool CanOpenInShell => _shellOpener is not null;

    internal void OpenInShell(string path) => _shellOpener?.Open(path);

    /// <summary>Whether no team exists at all, archived or not (D-04): the screen offers both ways in.</summary>
    public bool IsEmpty => Teams.Count == 0 && ArchivedTeams.Count == 0;

    /// <summary>
    /// Whether every team is archived (D-04): the list is empty, the teams are not — the empty state
    /// offers the archives rather than the ways in.
    /// </summary>
    public bool IsAllArchived => Teams.Count == 0 && ArchivedTeams.Count > 0 && !_showArchives;

    /// <summary>Whether the search found nothing where there were teams to find.</summary>
    public bool HasNoMatch =>
        Cards.Count == 0 && _searchText.Trim().Length > 0 && (_showArchives ? ArchivedTeams : Teams).Count > 0;

    /// <summary>« No team matches “abc”. » — said, with the way back; empty otherwise.</summary>
    public string NoMatchLine => HasNoMatch
        ? string.Format(CultureInfo.CurrentCulture, _strings[StudioStringKeys.TeamsSearchNoMatch], _searchText.Trim())
        : "";

    /// <summary>Whether resumable sessions are listed.</summary>
    public bool HasInProgress => InProgress.Count > 0;

    /// <summary>Opens the creation wizard.</summary>
    public RelayCommand CreateCommand { get; }

    /// <summary>Opens the import screen.</summary>
    public RelayCommand ImportCommand { get; }

    // ── the view (D-01) ──

    /// <summary>
    /// The search box: every word typed starts a word of a team's name or need, accents and case
    /// aside (<see cref="TextSearch"/>). Applied to the cards in memory, never through a refresh.
    /// </summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value ?? ""))
                return;

            // What the questions were about may leave the screen: they close with the change.
            CloseCardQuestions();
            RebuildView();
        }
    }

    /// <summary>Empties the search box.</summary>
    public RelayCommand ClearSearchCommand { get; }

    /// <summary>The order of the cards: the last activity by default, or the name.</summary>
    public TeamSortOrder SortOrder
    {
        get => _sortOrder;
        set
        {
            if (!SetProperty(ref _sortOrder, value))
                return;

            OnPropertiesChanged(nameof(IsSortedByActivity), nameof(IsSortedByName));
            RebuildView();
        }
    }

    /// <summary>Whether the cards come most recent activity first.</summary>
    public bool IsSortedByActivity => _sortOrder == TeamSortOrder.LastActivity;

    /// <summary>Whether the cards come by name.</summary>
    public bool IsSortedByName => _sortOrder == TeamSortOrder.Name;

    /// <summary>Orders the cards by last activity.</summary>
    public RelayCommand SortByActivityCommand { get; }

    /// <summary>Orders the cards by name.</summary>
    public RelayCommand SortByNameCommand { get; }

    /// <summary>
    /// Whether the screen lists the archived teams rather than the active ones. It exists while it
    /// has something to show: with nothing archived it cannot open, and the last restore or delete
    /// closes it.
    /// </summary>
    public bool ShowArchives
    {
        get => _showArchives;
        set
        {
            if (!SetProperty(ref _showArchives, value && ArchivedTeams.Count > 0))
                return;

            CloseCardQuestions();
            RebuildView();
            OnPropertiesChanged(nameof(IsAllArchived), nameof(HasArchiveSuggestion));
        }
    }

    /// <summary>« Archives (N) » — the Archives view, and back; also the « all archived » empty state's way on.</summary>
    public RelayCommand ToggleArchivesCommand { get; }

    // ── the undo banner (D-02) ──

    /// <summary>Whether the undo banner shows: an archive was made a moment ago.</summary>
    public bool HasUndo => _undo is not null;

    /// <summary>
    /// What the banner says: the team archived, the teams an accepted suggestion archived — or, after
    /// « Stop the schedule and archive », that the undo brings the team back and not its schedule.
    /// </summary>
    public string UndoNotice => _undo switch
    {
        null => "",
        [{ ScheduleStopped: true } team] => string.Format(
            CultureInfo.CurrentCulture, _strings[StudioStringKeys.TeamsArchivedScheduleStoppedUndo], team.Name),
        [var team] => string.Format(CultureInfo.CurrentCulture, _strings[StudioStringKeys.TeamsArchivedUndo], team.Name),
        var teams => string.Format(CultureInfo.CurrentCulture, _strings[StudioStringKeys.TeamsArchivedManyUndo], teams.Count),
    };

    /// <summary>« Undo » — every team the last archive gesture took out of the list comes back.</summary>
    public RelayCommand UndoCommand { get; }

    /// <summary>Closes the banner; the archive stands.</summary>
    public RelayCommand DismissUndoCommand { get; }

    // ── the archive suggestion (DB-1) ──

    /// <summary>
    /// The teams the suggestion lists, oldest activity first: active teams whose last activity is at
    /// least the threshold of Settings › Studio old — never a scheduled team, whose runs the system
    /// makes without Studio, nor one whose last activity is unknown, which nothing says is old.
    /// </summary>
    public IReadOnlyList<TeamCardViewModel> SuggestedTeams => _suggested;

    /// <summary>
    /// Whether the suggestion shows: something to propose, not answered yet this session, on the
    /// active teams — and nothing else asking on screen: a card's question or the undo banner come
    /// first, the suggestion steps aside until they are answered.
    /// </summary>
    public bool HasArchiveSuggestion =>
        _suggested.Count > 0 && !_suggestionAnswered && !_showArchives && _undo is null && !IsAnyQuestionOpen;

    /// <summary>« 3 teams not launched for 60 days — archive them? », or the one team by its name.</summary>
    public string ArchiveSuggestionLine => _suggested switch
    {
        [] => "",
        [var team] => string.Format(
            CultureInfo.CurrentCulture, _strings[StudioStringKeys.TeamsSuggestionOne], team.Name, _suggestionDays),
        var teams => string.Format(
            CultureInfo.CurrentCulture, _strings[StudioStringKeys.TeamsSuggestionMany], teams.Count, _suggestionDays),
    };

    /// <summary>The names of the teams listed, when there are several — exactly what « Archive » archives.</summary>
    public string ArchiveSuggestionNames => _suggested.Count > 1 ? string.Join(", ", _suggested.Select(card => card.Name)) : "";

    /// <summary>Archives the teams listed, and offers one undo for them all.</summary>
    public RelayCommand ArchiveSuggestedCommand { get; }

    /// <summary>« Not now » — the suggestion goes for this session; nothing is archived.</summary>
    public RelayCommand DismissSuggestionCommand { get; }

    /// <summary>Whether a card or a session row is asking something: a delete banner, a rename editor, an archive notice.</summary>
    private bool IsAnyQuestionOpen =>
        AllCards.Any(card => !card.IsIdle || card.HasArchiveNotice) || InProgress.Any(session => session.IsConfirmingDelete);

    /// <summary>A card or a session row opened or closed a question: the suggestion steps aside, or comes back.</summary>
    internal void OnQuestionChanged() => OnPropertyChanged(nameof(HasArchiveSuggestion));

    /// <summary>Re-reads both catalogs.</summary>
    public void Refresh()
    {
        Teams.Clear();
        ArchivedTeams.Clear();
        // Read once per refresh, not once per card: the settings list is the same for all of them.
        var declared = _declaredMounts();
        foreach (var team in _loadTeams())
            (team.IsArchived ? ArchivedTeams : Teams).Add(new TeamCardViewModel(team, this, _strings, declared));

        InProgress.Clear();
        foreach (var session in _loadSessions())
        {
            if (session.CanResume)
                InProgress.Add(new InProgressSessionViewModel(session, this));
        }

        ApplyLastRuns();
        ApplyScheduleStates();
        // The Archives view closes by itself once nothing is left in it: a restore or a delete took
        // the last archive away.
        if (ArchivedTeams.Count == 0 && _showArchives)
        {
            _showArchives = false;
            OnPropertyChanged(nameof(ShowArchives));
        }

        RebuildView();
        RefreshArchiveSuggestion();
        OnPropertiesChanged(
            nameof(ActiveCount), nameof(IsEmpty), nameof(IsAllArchived), nameof(HasInProgress),
            nameof(ArchivedCount), nameof(HasArchives), nameof(ArchivesLabel));
    }

    /// <summary>
    /// Rebuilds what the screen lists from the cards in memory (D-01): the active ones or the
    /// archives, those the search finds, in the chosen order. Never reads the disk.
    /// </summary>
    private void RebuildView()
    {
        var words = TextSearch.Words(_searchText);
        Cards = [.. Ordered((_showArchives ? ArchivedTeams : Teams).Where(card => TextSearch.Finds(words, card.SearchableText)))];
        OnPropertiesChanged(nameof(Cards), nameof(HasNoMatch), nameof(NoMatchLine));
    }

    /// <summary>
    /// The chosen order: the most recent activity first — a team whose activity is unknown last —
    /// or the name; the name, then the folder, settle a tie, so the order never depends on the disk.
    /// </summary>
    private IEnumerable<TeamCardViewModel> Ordered(IEnumerable<TeamCardViewModel> cards)
    {
        var byName = StringComparer.Create(CultureInfo.CurrentCulture, CompareOptions.IgnoreCase);
        var ordered = _sortOrder == TeamSortOrder.Name
            ? cards.OrderBy(card => card.Name, byName)
            : cards.OrderBy(card => card.LastActivity is null)
                .ThenByDescending(card => card.LastActivity)
                .ThenBy(card => card.Name, byName);
        return ordered.ThenBy(card => card.Slug, StringComparer.Ordinal);
    }

    /// <summary>
    /// Works the archive suggestion out again (DB-1) — the shell calls it when Settings › Studio
    /// change; the screen, whenever a team's activity or schedule may have. Nothing is proposed
    /// before the launch history was read: it may know a later run than the sidecars.
    /// </summary>
    public void RefreshArchiveSuggestion()
    {
        var settings = _studioSettings();
        _suggestionDays = settings.ArchiveSuggestionDays;
        var before = _clock.GetUtcNow() - TimeSpan.FromDays(settings.ArchiveSuggestionDays);
        _suggested = settings.ArchiveSuggestion && _lastRunsKnown
            ?
            [
                .. Teams
                    .Where(card => !card.HoldsSchedule && card.LastActivity is { } last && last <= before)
                    .OrderBy(card => card.LastActivity)
                    .ThenBy(card => card.Slug, StringComparer.Ordinal),
            ]
            : [];
        OnPropertiesChanged(
            nameof(SuggestedTeams), nameof(HasArchiveSuggestion), nameof(ArchiveSuggestionLine), nameof(ArchiveSuggestionNames));
        ArchiveSuggestedCommand.RaiseCanExecuteChanged();
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

        foreach (var path in AllCards.Where(card => card.Summary.HasSchedule).Select(card => card.Summary.Path).ToList())
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
        // A schedule the engine says runs takes the team out of the suggestion (DB-1).
        RefreshArchiveSuggestion();
    }

    private void ApplyScheduleStates()
    {
        foreach (var card in AllCards)
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

        // Read or unreadable, the history has said what it could: the order and the suggestion follow.
        _lastRunsKnown = true;
        RebuildView();
        RefreshArchiveSuggestion();
    }

    private void ApplyLastRuns()
    {
        foreach (var card in AllCards)
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

    /// <summary>
    /// The card's Test icon, which bypasses the trial screen's picker (STUDIO-31, D-07): an archived
    /// team is not tested from Studio — the card offers to restore it rather than refusing in silence.
    /// </summary>
    internal void RequestTest(TeamCardViewModel card)
    {
        if (card.IsArchived)
        {
            card.OfferRestore(_strings[StudioStringKeys.CommonArchivedTeamRestore]);
            return;
        }

        TestRequested?.Invoke(this, new TeamActionEventArgs(card.Summary.Path));
    }

    /// <summary>
    /// Archives a team (STUDIO-31): a flag in its sidecar, the folder where it is. Refused while the
    /// team is busy (D-09). A scheduled team — declared, or recorded as installed — is refused too,
    /// the card offering « Stop the schedule and archive » (D-06); that gesture stops the schedule
    /// through the engine first, the way a deletion does, and a refusal of the system keeps the team
    /// active, saying what to run by hand. Every refusal lands on the card. No question before it —
    /// it is undone in one click: the undo banner follows (STUDIO-32, D-02).
    /// </summary>
    internal async Task ArchiveAsync(TeamCardViewModel card, bool stopSchedule)
    {
        var team = card.Summary;
        // One question at a time: what this gesture has to say lands on this card, or in the banner.
        CloseQuestions();
        if (team.IsArchived)
            return;

        if (ActivityOf(team.Path) != TeamActivity.None)
        {
            card.ReportArchive(_strings[StudioStringKeys.TeamsArchiveBusy]);
            return;
        }

        var scheduleStopped = false;
        if (card.HoldsSchedule)
        {
            if (!stopSchedule)
            {
                card.OfferScheduleStop(_strings[StudioStringKeys.TeamsArchiveScheduled]);
                return;
            }

            var report = await ScheduleAsync(team.Path, ForgeScheduleVerb.Remove).ConfigureAwait(true);
            if (!report.Succeeded)
            {
                card.ReportArchive(
                    string.Format(CultureInfo.CurrentCulture, _strings[StudioStringKeys.TeamsArchiveUnscheduleFailed], report.FailureReason),
                    report.ManualCommand);
                return;
            }

            TeamCatalog.ClearSchedule(team.Path);
            _scheduleStates.Remove(NormalizePath(team.Path));
            scheduleStopped = true;
        }

        if (!TeamCatalog.Archive(team.Path, _clock.GetUtcNow()))
        {
            // A schedule stopped on the way changed the card: it is rebuilt, and says the refusal.
            if (scheduleStopped)
                Refresh();
            (CardFor(team.Path) ?? card).ReportArchive(_strings[StudioStringKeys.TeamsArchiveRefused]);
            return;
        }

        Refresh();
        ArchiveChanged?.Invoke(this, new ArchiveChangedEventArgs([team.Path]));
        OfferUndo([new ArchivedTeam(team.Path, card.Name, scheduleStopped, scheduleStopped ? team.Schedule : null)]);
    }

    /// <summary>
    /// Restores an archived team (STUDIO-31): null once it is back among the active teams — where the
    /// order puts it, not on top (D-10) — else the refusal: the team is busy (D-09), or the disk
    /// refused. A team that is not archived has nothing to restore. The launchers' « Restore » comes
    /// through here too, so the rules and the refresh of every list are the same whoever asks.
    /// </summary>
    public string? RestoreTeam(string teamPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamPath);

        if (!TeamCatalog.Describe(teamPath).IsArchived)
            return null;

        if (RestoreRefusal(teamPath) is { } refusal)
            return refusal;

        Refresh();
        ArchiveChanged?.Invoke(this, new ArchiveChangedEventArgs([teamPath]));
        return null;
    }

    /// <summary>The restore itself, the lists left as they are: the refusal — the team busy (D-09), the disk — or null once restored.</summary>
    private string? RestoreRefusal(string teamPath)
    {
        if (ActivityOf(teamPath) != TeamActivity.None)
            return _strings[StudioStringKeys.TeamsRestoreBusy];

        return TeamCatalog.Restore(teamPath) ? null : _strings[StudioStringKeys.TeamsRestoreRefused];
    }

    /// <summary>« Restore » on a card: <see cref="RestoreTeam"/>, the refusal said on the card.</summary>
    internal void Restore(TeamCardViewModel card)
    {
        CloseQuestions();
        if (RestoreTeam(card.Summary.Path) is { } refusal)
            card.ReportArchive(refusal);
    }

    /// <summary>
    /// One team an archive gesture took out of the list — what its undo puts back (STUDIO-32, D-02).
    /// </summary>
    /// <param name="Path">The team folder.</param>
    /// <param name="Name">The team's name, for the banner.</param>
    /// <param name="ScheduleStopped">Whether « Stop the schedule and archive » stopped its schedule on the way.</param>
    /// <param name="Schedule">
    /// The schedule the sidecar declared then, which the undo writes back: the user's choice returns,
    /// the registration the engine removed does not — nothing is reinstalled behind the user's back.
    /// </param>
    private sealed record ArchivedTeam(string Path, string Name, bool ScheduleStopped = false, string? Schedule = null);

    /// <summary>
    /// Shows the undo banner for <paramref name="archived"/>, for a few seconds (D-02), on the
    /// screen's own timer: dropping the banner cancels what is pending on it, and the chat's timer
    /// holds the assistant's beats.
    /// </summary>
    private void OfferUndo(IReadOnlyList<ArchivedTeam> archived)
    {
        _undoDelay.CancelPending();
        _undo = archived;
        RaiseUndoChanged();
        _undoDelay.After(UndoWindow, DropUndo);
    }

    /// <summary>Takes the undo banner away — its time is up, or another question opens; the archive stands.</summary>
    private void DropUndo()
    {
        _undoDelay.CancelPending();
        if (_undo is null)
            return;

        _undo = null;
        RaiseUndoChanged();
    }

    private void RaiseUndoChanged()
    {
        OnPropertiesChanged(nameof(HasUndo), nameof(UndoNotice), nameof(HasArchiveSuggestion));
        UndoCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// « Undo » (D-02): every team the last archive gesture took out of the list comes back, through
    /// the rules of a restore — a team busy by now stays archived, and the status line says why. After
    /// « Stop the schedule and archive », the team comes back with the schedule its sidecar declared,
    /// and its card says what the engine's removal left: not installed, with « Install the schedule ».
    /// </summary>
    private void Undo()
    {
        if (_undo is not { } archived)
            return;

        DropUndo();
        var restored = new List<string>();
        var refusals = new List<string>();
        foreach (var team in archived)
        {
            // Restored since, from the Archives view or a launcher: nothing left to undo.
            if (!TeamCatalog.Describe(team.Path).IsArchived)
                continue;

            if (RestoreRefusal(team.Path) is { } refusal)
            {
                refusals.Add(refusal);
                continue;
            }

            restored.Add(team.Path);
            if (team.ScheduleStopped)
                BringScheduleBack(team);
        }

        Refresh();
        if (restored.Count > 0)
            ArchiveChanged?.Invoke(this, new ArchiveChangedEventArgs(restored));
        if (refusals.Count > 0)
            StatusMessage = string.Join(" ", refusals.Distinct(StringComparer.Ordinal));
    }

    /// <summary>
    /// The undo of « Stop the schedule and archive »: the schedule the user chose is declared again in
    /// the sidecar, and the card shows the state the engine's removal left — not installed (STUDIO-27):
    /// the engine is asked nothing, so nothing runs the team until « Install the schedule » says so.
    /// </summary>
    private void BringScheduleBack(ArchivedTeam team)
    {
        if (team.Schedule is { Length: > 0 } schedule)
            TeamCatalog.UpdateMetadata(team.Path, metadata => metadata with { Schedule = schedule });

        _scheduleStates[NormalizePath(team.Path)] = TeamScheduleState.Absent;
    }

    /// <summary>
    /// « Archive » on the suggestion (DB-1): exactly the teams it lists, each read again at the click —
    /// one archived since is left as it is, one scheduled since offers to stop its schedule on its
    /// card, one running or open in the wizard is refused on its card. One undo brings back every team
    /// the click archived. Answered, the suggestion is not made again this session.
    /// </summary>
    private void ArchiveSuggested()
    {
        var listed = _suggested;
        _suggestionAnswered = true;
        CloseQuestions();

        var at = _clock.GetUtcNow();
        var archived = new List<ArchivedTeam>();
        var refusals = new List<(string Path, string Refusal, bool OffersScheduleStop)>();
        foreach (var card in listed)
        {
            var team = TeamCatalog.Describe(card.Summary.Path);
            if (team.IsArchived)
                continue;

            if (team.HasSchedule || card.HoldsSchedule)
                refusals.Add((team.Path, _strings[StudioStringKeys.TeamsArchiveScheduled], true));
            else if (ActivityOf(team.Path) != TeamActivity.None)
                refusals.Add((team.Path, _strings[StudioStringKeys.TeamsArchiveBusy], false));
            else if (!TeamCatalog.Archive(team.Path, at))
                refusals.Add((team.Path, _strings[StudioStringKeys.TeamsArchiveRefused], false));
            else
                archived.Add(new ArchivedTeam(team.Path, card.Name));
        }

        Refresh();
        foreach (var (path, refusal, offersScheduleStop) in refusals)
        {
            if (CardFor(path) is not { } refused)
                continue;

            if (offersScheduleStop)
                refused.OfferScheduleStop(refusal);
            else
                refused.ReportArchive(refusal);
        }

        if (archived.Count == 0)
            return;

        ArchiveChanged?.Invoke(this, new ArchiveChangedEventArgs([.. archived.Select(team => team.Path)]));
        OfferUndo(archived);
    }

    /// <summary>« Not now » (DB-1): the suggestion goes for this session, and nothing is archived.</summary>
    private void DismissSuggestion()
    {
        _suggestionAnswered = true;
        OnPropertyChanged(nameof(HasArchiveSuggestion));
    }

    /// <summary>
    /// Closes every question on screen before another opens (STUDIO-28's rule, STUDIO-32): the cards'
    /// delete banners, rename editors and archive notices, the sessions' delete banners, and the undo
    /// banner — two open at once would ask twice, and an answer could land where it was not meant.
    /// The suggestion is not closed: it steps aside while a question is open.
    /// </summary>
    private void CloseQuestions()
    {
        CloseCardQuestions();
        DropUndo();
    }

    /// <summary>The cards' and the sessions' questions — what a change of the view closes, the undo banner left alone.</summary>
    private void CloseCardQuestions()
    {
        foreach (var card in AllCards)
        {
            card.IsConfirmingDelete = false;
            card.IsRenaming = false;
            card.ClearArchiveNotice();
        }

        foreach (var session in InProgress)
            session.IsConfirmingDelete = false;
    }

    /// <summary>The card of <paramref name="teamPath"/>, active or archived; null when none shows it.</summary>
    private TeamCardViewModel? CardFor(string teamPath) =>
        AllCards.FirstOrDefault(card => string.Equals(
            NormalizePath(card.Summary.Path), NormalizePath(teamPath), StringComparison.OrdinalIgnoreCase));

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
        // The copy's arrival is its activity (STUDIO-32): a fresh copy of an old team is a fresh team.
        if (TeamCatalog.Duplicate(path, _clock.GetUtcNow()) is not { } copy)
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
        CloseQuestions();
        card.IsRenaming = true;
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

        if (card.HoldsSchedule)
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
    /// question twice, and the second answer would land on the wrong team. One question on
    /// screen at a time: a rename editor, an archive notice and the undo banner close too
    /// (STUDIO-28, STUDIO-32).
    /// </summary>
    internal void ArmDelete(object row)
    {
        CloseQuestions();
        switch (row)
        {
            case TeamCardViewModel card:
                // The banner's box needs the session before it opens (STUDIO-27, D-07).
                card.PrepareDelete(LinkedSessionOf(card.Summary));
                card.IsConfirmingDelete = true;
                break;
            case InProgressSessionViewModel session:
                session.IsConfirmingDelete = true;
                break;
        }
    }
}
