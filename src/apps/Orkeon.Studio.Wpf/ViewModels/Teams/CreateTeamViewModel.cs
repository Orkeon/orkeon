using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Orkeon.Studio.Core.Events;
using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.Launch;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Wpf.ViewModels.Config;
using Orkeon.Studio.Wpf.ViewModels.Launch;
using Orkeon.Studio.Wpf.ViewModels.Mounts;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Teams;

/// <summary>Payload of <see cref="CreateTeamViewModel.TeamAdopted"/>: the promoted folder.</summary>
public sealed class TeamAdoptedEventArgs(string path) : EventArgs
{
    /// <summary>Absolute path of the team folder.</summary>
    [SuppressMessage("Minor Code Smell", "S3604:Member initializer values should not be redundant",
        Justification = "False positive on a primary constructor: the initializer IS the only "
                      + "assignment of the member, and removing it would leave it unset.")]
    public string Path { get; } = path;
}

/// <summary>One selectable chip of the step-1 precisions.</summary>
public sealed class WizardChoice : ObservableObject
{
    private readonly Action<WizardChoice> _pick;
    private bool _isSelected;

    internal WizardChoice(string key, string label, Action<WizardChoice> pick)
    {
        Key = key;
        Label = label;
        _pick = pick;
        SelectCommand = new RelayCommand(() => _pick(this));
    }

    /// <summary>Stable identity of the choice (never localized).</summary>
    public string Key { get; }

    /// <summary>The chip text, in the user's language.</summary>
    public string Label { get; }

    /// <summary>Whether this chip is the picked one of its question.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        internal set => SetProperty(ref _isSelected, value);
    }

    /// <summary>Picks this chip.</summary>
    public RelayCommand SelectCommand { get; }
}

/// <summary>One agent card of the "Composer" step — a blueprint agent, or a step-group fallback.</summary>
public sealed class WizardAgentCard
{
    internal WizardAgentCard(string? key, string name, string initial, string role, IReadOnlyList<WizardToolChip> tools, Action<string?>? edit, Func<bool> canEdit)
    {
        Key = key;
        Name = name;
        Initial = initial;
        Role = role;
        Tools = tools;
        EditCommand = new RelayCommand(() => edit?.Invoke(key), () => edit is not null && key is not null && canEdit());
    }

    /// <summary>The blueprint key — expert mono; null for a step-group fallback card.</summary>
    public string? Key { get; }

    /// <summary>Display name (the blueprint's role).</summary>
    public string Name { get; }

    /// <summary>The round avatar's letter.</summary>
    public string Initial { get; }

    /// <summary>What the agent does, in a sentence.</summary>
    public string Role { get; }

    /// <summary>
    /// The agent's own tools — the « Peut : » chips. Labels and glyphs, never the engine's
    /// identifiers: «fs.read» says nothing to the person reading the card, «lire /docs» does.
    /// </summary>
    public IReadOnlyList<WizardToolChip> Tools { get; }

    /// <summary>Whether the card carries tool chips.</summary>
    public bool HasTools => Tools.Count > 0;

    /// <summary>« Modifier » — the agent editor over this agent.</summary>
    public RelayCommand EditCommand { get; }
}

/// <summary>One line of the trial checklist.</summary>
public sealed record WizardChecklistLine(string Statement, bool? Passed, string? Detail);

/// <summary>
/// One line of the trial's activity list.
/// <para>
/// It used to be a bare string, so the template had nothing to key on and every line — a
/// failed task included — was drawn with a green check. The sentence said «failed» while the
/// icon said «done».
/// </para>
/// </summary>
/// <param name="Text">What happened, in the user's words.</param>
/// <param name="Detail">The task id, when the role alone cannot tell two tasks apart.</param>
/// <param name="Success">Whether it went well — the icon and its colour follow.</param>
public sealed record WizardActivityLine(string Text, string Detail, bool Success)
{
    /// <summary>Whether the task id is worth showing beside the role.</summary>
    public bool HasDetail => Detail.Length > 0;
}

/// <summary>One arbitration button, generated from the engine's own options.</summary>
public sealed class WizardDecision
{
    internal WizardDecision(string value, string label, Action<string> decide)
    {
        Value = value;
        Label = label;
        DecideCommand = new RelayCommand(() => decide(value));
    }

    /// <summary>Wire spelling sent back to the engine.</summary>
    public string Value { get; }

    /// <summary>Localized button text.</summary>
    public string Label { get; }

    /// <summary>Sends this arbitration.</summary>
    public RelayCommand DecideCommand { get; }
}

/// <summary>
/// What « Autoriser un dossier » and « Choisir le dossier… » ask of the shell.
/// </summary>
/// <remarks>
/// <c>TargetVirtualPath</c> null means «add a folder as the settings declare it». The
/// difference is the whole feature: without a target the chooser can only ever add the root
/// the settings named, which is never the root the blueprint implied.
/// </remarks>
public sealed class AllowFolderRequestedEventArgs(string? targetVirtualPath) : EventArgs
{
    /// <summary>The mount point to bind; null to add a folder as the settings declare it.</summary>
    [SuppressMessage("Minor Code Smell", "S3604:Member initializer values should not be redundant",
        Justification = "False positive on a primary constructor: the initializer IS the only "
                      + "assignment of the member, and removing it would leave it unset.")]
    public string? TargetVirtualPath { get; } = targetVirtualPath;
}

/// <summary>
/// One line of the team's folders: a mount point, who addresses it, and what sits behind it.
/// <para>
/// The card used to be two rows of chips — the roots the blueprint implies, and the folders
/// the user allowed — with no way to say that a given folder sits behind a given root. So an
/// implied root could only ever be answered at adoption, by a folder created inside the team
/// and left empty. One line per mount point is what makes the answer sayable: the name the
/// agents use on the left, the folder on the right, and «Choose the folder…» when there is
/// none yet.
/// </para>
/// </summary>
/// <param name="VirtualPath">The name the agents address, e.g. <c>/workspace</c>.</param>
/// <param name="IsReadWrite">Whether the team writes there, or only reads.</param>
/// <param name="Agents">Who addresses it, joined for display — PROVENANCE, never permission.</param>
/// <param name="Folder">The folder behind it; empty while the mount point is unanswered.</param>
/// <param name="MountString">
/// The team mount this row stands for, for removal; empty on a row the blueprint implies and
/// nothing backs yet — such a row has nothing to remove but its own root.
/// </param>
/// <param name="IsUndeclared">Whether the bound folder is outside the settings' authorized list.</param>
/// <param name="IsUnreadable">
/// Whether the entry behind this row is a mount string the parser refuses. Such a row has no
/// virtual spelling of its own (ADR-008), so it names no mount point that could be answered:
/// all it can offer is its own removal.
/// </param>
public sealed record MountRow(
    string VirtualPath,
    bool IsReadWrite,
    string Agents = "",
    string Folder = "",
    string MountString = "",
    bool IsUndeclared = false,
    bool IsUnreadable = false)
{
    /// <summary>Whether the row can say who addresses this mount point.</summary>
    public bool HasAgents => Agents.Length > 0;

    /// <summary>Whether a folder is bound behind it.</summary>
    public bool HasFolder => Folder.Length > 0;

    /// <summary>Whether the row stands for a folder the user bound, rather than a bare implied root.</summary>
    public bool IsBound => MountString.Length > 0;

    /// <summary>
    /// Whether the row offers « Choisir le dossier… ». An unreadable entry does not: its
    /// displayed name is a message, not a virtual path, and targeting the chooser at it would
    /// bind the picked folder behind that message.
    /// </summary>
    public bool CanChooseFolder => !HasFolder && !IsUnreadable;
}

/// <summary>
/// The wizard's seams: the collaborators it otherwise builds itself. They travel as one
/// record rather than as seven constructor parameters — a test names the two or three it
/// cares about and leaves the rest to the real engine, the real clock and the real folders.
/// </summary>
public sealed record CreateTeamDependencies
{
    /// <summary>The forge engine channel; a client for the current machine when null.</summary>
    public ForgeClient? Client { get; init; }

    /// <summary>Where a continuation lands; the immediate (same-thread) dispatcher when null.</summary>
    public IUiDispatcher? Dispatcher { get; init; }

    /// <summary>The localized strings; English when null.</summary>
    public IStudioStrings? Strings { get; init; }

    /// <summary>Where the forge sessions live; the process working directory when null.</summary>
    public string? WorkspaceDirectory { get; init; }

    /// <summary>Where an adopted team lands; the default teams root when null.</summary>
    public string? TeamsRoot { get; init; }

    /// <summary>The folders the settings declare; none when null.</summary>
    public Func<IReadOnlyList<string>>? DeclaredMounts { get; init; }

    /// <summary>The window's conversation; a private thread when null.</summary>
    public ChatThreadViewModel? Chat { get; init; }
}

/// <summary>
/// The "create a team" wizard (design v3): four steps — Describe, Compose, Try,
/// Adopt — over the forge engine's event stream. The engine owns the cycle; the stepper
/// is a projection of its milestones, and every gesture here is one of the engine's own
/// (a brief, a message, an arbitration, a promotion). The wizard is gated on Studio's
/// assistant profile: composing a team and judging a trial are LLM work, and the engine
/// child receives that profile as environment overrides.
/// </summary>
public sealed class CreateTeamViewModel : ObservableObject
{
    private readonly ForgeClient _client;
    private readonly Func<IReadOnlyList<string>> _declaredMounts;
    /// <summary>Agent-addressed roots the user dropped; nothing will be bound to them at save.</summary>
    private readonly HashSet<string> _droppedDerivedRoots = new(StringComparer.Ordinal);
    private readonly IUiDispatcher _dispatcher;
    private readonly IStudioStrings _strings;
    private readonly string _workspace;
    private readonly string _teamsRoot;
    private ForgeSessionModel _model = new();
    private int _runGeneration;
    private string? _saveError;
    private int _step = 1;
    private int _maxStep = 1;
    private string _need = "";
    private string _outcome = "";
    private string _statusMessage = "";
    private bool _isEngineRunning;
    private bool _isTechOpen;
    private string _teamName = "";
    private int _scheduleChoice;
    private string _scheduleTime = "07:30";
    private string? _adoptProfileName;
    private bool _isAdoptProfilePickerOpen;
    private bool _isSaved;
    private string? _reopenedTeamPath;
    private bool _autoRetryPending;
    private string? _lastStderr;

    /// <summary>Builds the wizard; every collaborator is optional so tests inject doubles.</summary>
    public CreateTeamViewModel(ModelProfilesViewModel profiles, CreateTeamDependencies? dependencies = null)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        var wired = dependencies ?? new CreateTeamDependencies();
        Profiles = profiles;
        _declaredMounts = wired.DeclaredMounts ?? (() => []);
        _client = wired.Client ?? ForgeClient.ForCurrentMachine();
        _dispatcher = wired.Dispatcher ?? ImmediateUiDispatcher.Instance;
        _strings = wired.Strings ?? EnglishStudioStrings.Instance;
        _workspace = wired.WorkspaceDirectory ?? Environment.CurrentDirectory;
        _teamsRoot = wired.TeamsRoot ?? TeamCatalog.DefaultRoot();

        // The conversation is the window's, not this screen's: it has to survive a tab
        // change, and losing it on the first one is precisely the defect being fixed. A
        // test that does not care gets a private one rather than a null check everywhere.
        Chat = wired.Chat ?? new ChatThreadViewModel(_strings);

        RawLog = new RunLogViewModel(_strings);
        AgentEditor = new AgentEditorViewModel(_strings);
        AddAgentCommand = new RelayCommand(() => EditAgent(null), () => CanEditAgents);
        AllowFolderCommand = new RelayCommand(() => RequestAllowFolder(null));
        BindMountCommand = new RelayCommand(BindMount, IsStringParameter);
        ToggleAdoptProfilePickerCommand = new RelayCommand(() => IsAdoptProfilePickerOpen = !IsAdoptProfilePickerOpen);
        PickAdoptProfileCommand = new RelayCommand(PickAdoptProfile);
        RemoveTeamMountCommand = new RelayCommand(RemoveTeamMount, IsStringParameter);
        RemoveDerivedMountCommand = new RelayCommand(DropDerivedMount, IsStringParameter);
        RestoreDerivedMountsCommand = new RelayCommand(
            RestoreDerivedMounts,
            () => _droppedDerivedRoots.Count > 0);
        Progress = new ComposeProgressViewModel(_strings);
        // The count is the conversation's, read live: three per-step mini-threads were
        // replaced by one thread, so each block reports on that one rather than on its own.
        ComposeNotes = new StepNotesViewModel(ChatMessageCount);
        TryNotes = new StepNotesViewModel(ChatMessageCount);
        AdoptNotes = new StepNotesViewModel(ChatMessageCount);

        FrequencyChoices = Choices(
            (StudioStringKeys.WizardFreqOnce, "once"),
            (StudioStringKeys.WizardFreqDaily, "daily"),
            (StudioStringKeys.WizardFreqWeekly, "weekly"));
        SourceChoices = Choices(
            (StudioStringKeys.WizardSourceFolder, "folder"),
            (StudioStringKeys.WizardSourceWeb, "web"),
            (StudioStringKeys.WizardSourceUnknown, "unknown"));
        OutputChoices = Choices(
            (StudioStringKeys.WizardOutputDocument, "document"),
            (StudioStringKeys.WizardOutputTable, "table"),
            (StudioStringKeys.WizardOutputMessage, "message"),
            (StudioStringKeys.WizardOutputOther, "other"));

        // Bound after the choice groups exist: the recap and the brief chips read them.
        Chat.Bind(
            facts: BuildRecapFacts,
            brief: () => _need.Trim(),
            briefChips: () =>
            [
                .. new[] { FrequencyChoices, SourceChoices, OutputChoices }
                    .Select(group => group.FirstOrDefault(c => c.IsSelected)?.Label)
                    .Where(label => label is { Length: > 0 })
                    .Select(label => label!),
            ],
            profileName: () => Profiles.Set.Studio?.Name,
            askEngine: AskEngine);
        Chat.StopRequested += (_, _) => StopEngine();
        // «Edit» on the brief card means the form, not just a hidden panel. Without this the
        // pencil closed the thread and dropped the user back on whatever step they were on,
        // with the brief as unreachable as it was a moment earlier.
        Chat.EditBriefRequested += (_, _) => GoStep(1);
        Chat.Turns.CollectionChanged += (_, _) => OnChatTurnsChanged();
        Chat.PropertyChanged += (_, e) => OnChatPropertyChanged(e.PropertyName);

        // The gesture IS the engine. The questions belong to the model, asked during its
        // brief stage; a local questionnaire played first only interviewed the user twice.
        ComposeCommand = new AsyncRelayCommand(ComposeAsync, () => CanCompose);
        TryTeamCommand = new AsyncRelayCommand(TryTeamAsync, () => CanTryTeam);
        AdoptWithoutTrialCommand = new AsyncRelayCommand(AdoptWithoutTrialAsync, () => CanTryTeam);
        ReopenComposeCommand = new AsyncRelayCommand(() => ReopenAdoptedAsync(step: 2, autoRetry: false), () => IsSaved);
        RetryTrialCommand = new AsyncRelayCommand(() => ReopenAdoptedAsync(step: 3, autoRetry: true), () => IsSaved);
        UseExampleCommand = new RelayCommand(p => Need = p as string ?? Need);
        RestartCommand = new RelayCommand(
            Restart,
            // A started conversation is a creation under way too, even at step 1:
            // abandoning it has to be possible without first reaching step 2.
            // Not IsStarted: an engine that died mid-interview clears it, and the user
            // would be left staring at a conversation with no way to abandon it. Anything
            // said is a creation under way.
            () => MaxStep > 1 || IsEngineRunning || !Chat.IsEmpty);
        StopCommand = new RelayCommand(() => _client.RequestCancellation(), () => IsEngineRunning);
        GoStep1Command = new RelayCommand(() => GoStep(1));
        GoStep2Command = new RelayCommand(() => GoStep(2));
        GoStep3Command = new RelayCommand(() => GoStep(3));
        GoStep4Command = new RelayCommand(() => GoStep(4));
        SaveTeamCommand = new AsyncRelayCommand(SaveTeamAsync, () => CanSaveTeam);
        OpenSettingsCommand = new RelayCommand(() => OpenSettingsRequested?.Invoke(this, EventArgs.Empty));
        PickAssistantCommand = new RelayCommand(PickAssistant);

        Profiles.PropertyChanged += (_, e) => OnProfilesPropertyChanged(e.PropertyName);
    }

    // ── what the constructor wired ──────────────────────────────────────────
    // The wiring above says what is connected to what; each handler below says
    // what it then does. They are methods rather than inline lambdas so that the
    // constructor stays a list of connections, readable in one pass.

    private static bool IsStringParameter(object? parameter) => parameter is string;

    /// <summary>Asks the shell for a folder — behind <paramref name="targetVirtualPath"/>, or as the settings declare it.</summary>
    private void RequestAllowFolder(string? targetVirtualPath) =>
        AllowFolderRequested?.Invoke(this, new AllowFolderRequestedEventArgs(targetVirtualPath));

    private void BindMount(object? parameter)
    {
        if (parameter is string virtualPath)
            RequestAllowFolder(virtualPath);
    }

    private void PickAdoptProfile(object? parameter)
    {
        if (parameter is not string profileName)
            return;

        AdoptProfileName = profileName;
        IsAdoptProfilePickerOpen = false;
    }

    private void RemoveTeamMount(object? parameter)
    {
        if (parameter is not string mount)
            return;

        TeamMounts.Remove(mount);
        RefreshMountSurfaces();
    }

    private void DropDerivedMount(object? parameter)
    {
        if (parameter is string virtualPath && _droppedDerivedRoots.Add(virtualPath))
            RefreshMountSurfaces();
    }

    private void RestoreDerivedMounts()
    {
        _droppedDerivedRoots.Clear();
        RefreshMountSurfaces();
    }

    private void PickAssistant(object? parameter)
    {
        if (parameter is string name)
            Profiles.StudioProfileName = name;
    }

    /// <summary>
    /// Sends a free question to the engine's stdin. The projection must hear what the user
    /// said too, or a resumed session rebuilds a conversation with every question and none
    /// of the answers.
    /// </summary>
    private bool AskEngine(string question)
    {
        if (!_client.SendMessage(question))
            return false;

        _model.AddUserMessage(question);
        return true;
    }

    private void StopEngine()
    {
        if (IsEngineRunning)
            _client.RequestCancellation();
    }

    private void OnChatTurnsChanged()
    {
        RaiseDraftChanged();
        ComposeNotes.RefreshMessageCount();
        TryNotes.RefreshMessageCount();
        AdoptNotes.RefreshMessageCount();
    }

    private void OnChatPropertyChanged(string? propertyName)
    {
        if (propertyName is nameof(ChatThreadViewModel.IsStarted))
            RestartCommand?.RaiseCanExecuteChanged();

        // The nav's draft block reads the conversation too: the assistant awaiting your
        // reply is the state a user must never walk away from without seeing.
        if (propertyName is nameof(ChatThreadViewModel.IsStarted)
            or nameof(ChatThreadViewModel.IsBusy)
            or nameof(ChatThreadViewModel.IsAsking)
            or nameof(ChatThreadViewModel.UnreadCount))
        {
            RaiseDraftChanged();
        }

        // The brief interview is an engine blocked on stdin: while a question stands,
        // the top-of-screen spinner must stop claiming the engine is working.
        if (propertyName is nameof(ChatThreadViewModel.IsAsking))
            OnPropertiesChanged(nameof(IsEngineWaitingOnUser), nameof(IsEngineWorking), nameof(TrialInProgress));
    }

    private void OnProfilesPropertyChanged(string? propertyName)
    {
        if (propertyName is not (nameof(ModelProfilesViewModel.HasStudioProfile) or nameof(ModelProfilesViewModel.Set)))
            return;

        // CanCompose starts with HasAssistant: an election must wake the button too.
        OnPropertiesChanged(nameof(HasAssistant), nameof(NeedsAssistant), nameof(CanCompose), nameof(Step1Hint));
        ComposeCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Raised when a session becomes active — the shell brings the screen forward.</summary>
    public event EventHandler? SessionActivated;

    /// <summary>Raised when a team lands in the teams folder.</summary>
    public event EventHandler<TeamAdoptedEventArgs>? TeamAdopted;

    /// <summary>Raised by the "manage settings" button — the shell shows the settings screen.</summary>
    public event EventHandler? OpenSettingsRequested;

    /// <summary>The model profiles — the gate and the adoption picker read them.</summary>
    public ModelProfilesViewModel Profiles { get; }

    /// <summary>Level 3: the raw stream — stderr, stray lines, every protocol line.</summary>
    public RunLogViewModel RawLog { get; }

    /// <summary>
    /// Which engine build answered — «1.0.0-rc.2», or empty until a session says.
    /// <para>
    /// Studio does not embed the engine: it launches whichever <c>orkeon</c> its locator
    /// finds first, which may be co-installed, on PATH, or built from this checkout. Naming
    /// it here is what lets a reader tell a screen that shows nothing because the engine
    /// reported nothing from one that shows nothing because the engine is old.
    /// </para>
    /// </summary>
    public string EngineVersion => _model.EngineVersion ?? string.Empty;

    /// <summary>Whether an engine build is known — gates the version on the assistant line.</summary>
    public bool HasEngineVersion => EngineVersion.Length > 0;

    /// <summary>«moteur 1.0.0-rc.2», ready to sit next to the assistant's name.</summary>
    public string EngineLabel => HasEngineVersion
        ? string.Format(
            CultureInfo.CurrentCulture, _strings[StudioStringKeys.ComposeEngineVersion], EngineVersion)
        : string.Empty;

    /// <summary>The "Consigne de composition" block.</summary>
    /// <summary>
    /// The card that fills the wait on steps 2 and 3 — what the engine is doing, what the
    /// model last said, and what it has cost, up and down.
    /// </summary>
    public ComposeProgressViewModel Progress { get; }

    public StepNotesViewModel ComposeNotes { get; }

    /// <summary>The "Trial instruction" block.</summary>
    public StepNotesViewModel TryNotes { get; }

    /// <summary>The "Consigne d'adoption" block.</summary>
    public StepNotesViewModel AdoptNotes { get; }

    // ── gate ──

    /// <summary>True once Studio's assistant has a model profile.</summary>
    public bool HasAssistant => Profiles.HasStudioProfile;

    /// <summary>The gate card's visibility.</summary>
    public bool NeedsAssistant => !HasAssistant;

    // ── stepper ──

    /// <summary>The showing step, 1-based.</summary>
    public int Step
    {
        get => _step;
        private set
        {
            if (!SetProperty(ref _step, value))
                return;

            OnPropertiesChanged(nameof(IsStep1), nameof(IsStep2), nameof(IsStep3), nameof(IsStep4));
            RaiseDraftChanged();
        }
    }

    /// <summary>The furthest step the session reached; earlier steps stay clickable.</summary>
    public int MaxStep
    {
        get => _maxStep;
        private set
        {
            if (SetProperty(ref _maxStep, value))
                RestartCommand?.RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// The conversation with the assistant — the window's, shared with Run and
    /// Historique, so leaving this screen never empties it.
    /// </summary>
    public ChatThreadViewModel Chat { get; }

    // ── the draft, as the navigation sees it (30/08 mock, T-09) ──

    /// <summary>
    /// Whether an unfinished creation exists: something typed, a step passed, or the
    /// assistant mid-thread. Leaving the wizard must never be the same as losing it.
    /// </summary>
    public bool HasDraft =>
        !IsSaved && (_step > 1 || _need.Trim().Length > 0 || IsEngineRunning || !Chat.IsEmpty);

    /// <summary>Whether the assistant is the one holding the draft up.</summary>
    public bool IsAssistantWaiting =>
        HasDraft && (IsEngineRunning || Chat.IsBusy || Chat.IsAsking || Chat.HasUnread);

    /// <summary>The nav entry's own counter — «2/4», mono, no chip.</summary>
    public string DraftStepShort =>
        string.Create(CultureInfo.CurrentCulture, $"{_step}/{StepCount}");

    /// <summary>Title of the block under the nav entry.</summary>
    public string DraftTitle => _strings[IsAssistantWaiting
        ? StudioStringKeys.WizardDraftWaitingTitle
        : StudioStringKeys.WizardDraftTitle];

    /// <summary>
    /// The step line — number, total and stage name assembled from a per-culture pattern — Chinese has no
    /// word for the "of" joiner, and a concatenation would ship one culture's joiner everywhere.
    /// </summary>
    public string DraftLine
    {
        get
        {
            var tail = _strings[StepNameKey(_step)];

            // Waiting wins over counting: «reprendre la discussion» is what to DO, the
            // message count is only what there is. Never both on one line.
            if (IsAssistantWaiting)
                tail = Join(tail, _strings[StudioStringKeys.WizardDraftResume]);
            else if (Chat.Turns.Count > 0)
                tail = Join(tail, string.Format(
                    CultureInfo.CurrentCulture,
                    _strings[StudioStringKeys.ChatStatusMessagesPattern],
                    Chat.Turns.Count));

            return string.Format(
                CultureInfo.CurrentCulture,
                _strings[StudioStringKeys.WizardDraftStepPattern],
                _step, StepCount, tail);
        }
    }

    private string Join(string left, string right) => string.Format(
        CultureInfo.CurrentCulture, _strings[StudioStringKeys.WizardDraftJoinerPattern], left, right);

    private const int StepCount = 4;

    private static string StepNameKey(int step) => step switch
    {
        2 => StudioStringKeys.WizardStep2,
        3 => StudioStringKeys.WizardStep3,
        4 => StudioStringKeys.WizardStep4,
        _ => StudioStringKeys.WizardStep1,
    };

    private void RaiseDraftChanged() => OnPropertiesChanged(
        nameof(HasDraft), nameof(IsAssistantWaiting),
        nameof(DraftStepShort), nameof(DraftTitle), nameof(DraftLine));

    /// <summary>Step visibilities.</summary>
    public bool IsStep1 => _step == 1;

    /// <summary>Step 2 — Composer.</summary>
    public bool IsStep2 => _step == 2;

    /// <summary>Step 3 — Essayer.</summary>
    public bool IsStep3 => _step == 3;

    /// <summary>Step 4 — Adopter.</summary>
    public bool IsStep4 => _step == 4;

    /// <summary>Shows a reached step again.</summary>
    public RelayCommand GoStep1Command { get; }

    /// <summary>Shows step 2, once reached.</summary>
    public RelayCommand GoStep2Command { get; }

    /// <summary>Shows step 3, once reached.</summary>
    public RelayCommand GoStep3Command { get; }

    /// <summary>Shows step 4, once reached.</summary>
    public RelayCommand GoStep4Command { get; }

    // ── step 1 : Describe ──

    /// <summary>The need, in the user's words.</summary>
    public string Need
    {
        get => _need;
        set
        {
            if (SetProperty(ref _need, value))
            {
                OnPropertiesChanged(nameof(CanCompose), nameof(Step1Hint));
                RaiseDraftChanged();
                ComposeCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>The four example problems of the start page.</summary>
    public IReadOnlyList<string> Examples =>
    [
        _strings[StudioStringKeys.ForgeExample1],
        _strings[StudioStringKeys.ForgeExample2],
        _strings[StudioStringKeys.ForgeExample3],
        _strings[StudioStringKeys.ForgeExample4],
    ];

    /// <summary>The "how often" chips.</summary>
    public IReadOnlyList<WizardChoice> FrequencyChoices { get; }

    /// <summary>The "where the information lives" chips.</summary>
    public IReadOnlyList<WizardChoice> SourceChoices { get; }

    /// <summary>The "what the team must produce" chips.</summary>
    public IReadOnlyList<WizardChoice> OutputChoices { get; }

    /// <summary>Free description of the expected result; required when the format is free.</summary>
    public string Outcome
    {
        get => _outcome;
        set
        {
            if (SetProperty(ref _outcome, value))
            {
                OnPropertiesChanged(nameof(CanCompose), nameof(Step1Hint));
                ComposeCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>The free format makes the description mandatory — it is all the engine gets.</summary>
    public bool OutcomeRequired => OutputChoices.FirstOrDefault(c => c.IsSelected)?.Key == "other";

    /// <summary>Whether the brief is complete enough to compose.</summary>
    public bool CanCompose =>
        HasAssistant
        && !IsEngineRunning
        && _need.Trim().Length > 0
        && FrequencyChoices.Any(c => c.IsSelected)
        && SourceChoices.Any(c => c.IsSelected)
        && OutputChoices.Any(c => c.IsSelected)
        && (!OutcomeRequired || _outcome.Trim().Length > 0);

    /// <summary>The sentence under the compose button, tracking what is still missing.</summary>
    public string Step1Hint
    {
        get
        {
            if (_need.Trim().Length == 0)
                return _strings[StudioStringKeys.WizardHintDescribe];

            if (OutcomeRequired && _outcome.Trim().Length == 0)
                return _strings[StudioStringKeys.WizardHintOutcome];

            // A greyed button under «Everything is there — I can compose the team» is a lie:
            // the engine is already composing, and what it wants is an answer in the thread.
            if (IsEngineRunning)
                return _strings[StudioStringKeys.WizardHintComposing];

            if (!CanCompose)
                return _strings[StudioStringKeys.WizardHintAnswers];

            return _strings[StudioStringKeys.WizardHintReady];
        }
    }

    /// <summary>Fills the need box from an example.</summary>
    public RelayCommand UseExampleCommand { get; }

    /// <summary>Sends the brief to the engine — the wizard's "compose the team" button.</summary>
    public AsyncRelayCommand ComposeCommand { get; }

    // ── the engine ──

    /// <summary>Whether the engine child is alive.</summary>
    public bool IsEngineRunning
    {
        get => _isEngineRunning;
        private set
        {
            if (!SetProperty(ref _isEngineRunning, value))
                return;

            // CanTryTeam gates BOTH answers to the dry pause, and it reads IsEngineRunning:
            // the engine dying is the moment those two buttons become offerable, and it is
            // silent on the event stream — nothing else would raise them.
            OnPropertiesChanged(
                nameof(CanCompose), nameof(CanSaveTeam), nameof(IsEngineWorking),
                nameof(TrialInProgress), nameof(CanTryTeam));
            // The card's first and last readings come from here: the engine starting and
            // the engine dying are both silent on the event stream, and both change what
            // the card must say.
            SyncProgress();
            RaiseDraftChanged();
            ComposeCommand.RaiseCanExecuteChanged();
            StopCommand.RaiseCanExecuteChanged();
            RestartCommand.RaiseCanExecuteChanged();
            SaveTeamCommand.RaiseCanExecuteChanged();
            TryTeamCommand.RaiseCanExecuteChanged();
            AdoptWithoutTrialCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// Whether the engine is blocked on the user rather than working.
    /// <para>
    /// Read from the model, not from <c>DecisionPending</c> below: the ViewModel's copy skips
    /// the <c>edit</c> option, so an edit-only arbitration would read as «not waiting».
    /// </para>
    /// </summary>
    public bool IsEngineWaitingOnUser => _model.DecisionPending || Chat.IsAsking;

    /// <summary>
    /// Whether the trial is genuinely under way.
    /// <para>
    /// <c>RunInProgress</c> alone is set by <c>run.started</c> and cleared only by
    /// <c>run.finished</c> — which never arrives if the child dies, so the indicator stayed
    /// lit for ever after a crash and only «Recommencer» put it out. Requiring the engine to
    /// be working covers the crash, the kill and the arbitration in one predicate.
    /// </para>
    /// </summary>
    public bool TrialInProgress => RunInProgress && IsEngineWorking;

    /// <summary>
    /// Whether the engine is actually working. <see cref="IsEngineRunning"/> only means the
    /// child process is alive — and it is very much alive while blocked on stdin, waiting for
    /// an arbitration or an answer. A spinner turning then tells the user to keep waiting for
    /// something that is waiting for them.
    /// </summary>
    public bool IsEngineWorking => IsEngineRunning && !IsEngineWaitingOnUser;

    /// <summary>Plain status sentence (engine refusals land here in clear words).</summary>
    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>The shell's way in: a resume failure must land on this screen's status line.</summary>
    internal void ReportStatus(string message) => StatusMessage = message;

    /// <summary>The engine's pending arbitrations, as buttons; empty while none is owed.</summary>
    public ObservableCollection<WizardDecision> Decisions { get; } = [];

    /// <summary>Whether an arbitration is owed.</summary>
    public bool DecisionPending => Decisions.Count > 0;

    /// <summary>Stops the engine; the session stays resumable from the my-teams screen.</summary>
    public RelayCommand StopCommand { get; }

    /// <summary>Back to a blank step 1 (a running engine is asked to stop first).</summary>
    public RelayCommand RestartCommand { get; }

    /// <summary>The technical journal, folded by default.</summary>
    public bool IsTechOpen
    {
        get => _isTechOpen;
        set => SetProperty(ref _isTechOpen, value);
    }

    /// <summary>Session slug — expert only.</summary>
    public string? SessionSlug => _model.Slug;

    /// <summary>Session directory — expert only.</summary>
    public string? SessionDirectory => _model.Directory;

    /// <summary>
    /// The rendered crew YAML, concatenated for the generated-definition card (v3 W-06:
    /// the card shows the definition itself; the path retreats to a tooltip).
    /// </summary>
    public string CrewDefinitionYaml =>
        _model.Directory is { Length: > 0 } directory ? ForgeRenderReader.ReadDefinition(directory) : "";

    /// <summary>Whether the render produced anything to show.</summary>
    public bool HasCrewDefinition => CrewDefinitionYaml.Length > 0;

    // ── step 2 : Composer ──

    /// <summary>The agent cards, grouped from the proposal's steps by role.</summary>
    public ObservableCollection<WizardAgentCard> Agents { get; } = [];

    /// <summary>The agent editor modal (remediation v2, F-01).</summary>
    public AgentEditorViewModel AgentEditor { get; }

    /// <summary>
    /// The team-folders list — the mounts the adoption will record in the sidecar.
    /// The trial itself runs on the forge bench's own sandbox mounts; these describe what
    /// the adopted team will be allowed to see.
    /// </summary>
    public ObservableCollection<string> TeamMounts { get; } = [];

    /// <summary>Whether any team mount is listed.</summary>
    public bool HasTeamMounts => TeamMounts.Count > 0;

    /// <summary>
    /// The mounts the blueprint itself implies (v3 W-04). They ARE recorded in the sidecar at
    /// adoption (<see cref="WithDerivedWriteMounts"/>), bound to folders inside the team: this
    /// doc used to say the opposite, which is how removing an explicit <c>/output</c> chip
    /// could look like a choice and be undone at save without a word.
    /// </summary>
    public IReadOnlyList<ForgeDerivedMount> DerivedMounts => _model.DerivedMounts;

    /// <summary>Whether the blueprint implies any mount point no folder of yours backs yet.</summary>
    public bool HasDerivedMounts => MountRows.Any(r => !r.IsBound);

    /// <summary>
    /// The team's folders, one line per mount point: what the blueprint implies and what the
    /// user bound, merged on the name the agents actually use.
    /// <para>
    /// A mount the user bound wins its root — that binding IS the answer to the implied one,
    /// and showing both would show the same mount point twice. A root the user dropped
    /// appears in neither: it has its own banner, which says what dropping it costs.
    /// </para>
    /// </summary>
    public IReadOnlyList<MountRow> MountRows
    {
        get
        {
            var declared = _declaredMounts();
            var rows = new List<MountRow>();
            var claimed = new HashSet<string>(StringComparer.Ordinal);

            foreach (var mountString in TeamMounts)
            {
                if (!MountDefinition.TryParse(mountString, out var mount, out _) || mount is null)
                {
                    // ADR-008: an entry the parser refuses has no virtual spelling, and its
                    // raw form carries the folder on this machine. It still gets a line —
                    // silently dropping it would hide a mount the team will actually carry.
                    rows.Add(new MountRow(
                        MountLabels.Unreadable(_strings), IsReadWrite: false,
                        MountString: mountString, IsUndeclared: true, IsUnreadable: true));
                    continue;
                }

                claimed.Add(mount.VirtualPath);
                rows.Add(new MountRow(
                    mount.VirtualPath,
                    mount.Rights == MountRights.ReadWrite,
                    Agents: AgentsOf(mount.VirtualPath),
                    Folder: mount.PhysicalPath,
                    MountString: mountString,
                    IsUndeclared: !MountLabels.IsDeclared(mountString, declared)));
            }

            foreach (var derived in _model.DerivedMounts)
            {
                if (claimed.Contains(derived.VirtualPath) || _droppedDerivedRoots.Contains(derived.VirtualPath))
                    continue;

                rows.Add(new MountRow(
                    derived.VirtualPath,
                    derived.IsReadWrite,
                    Agents: string.Join(", ", derived.Agents ?? [])));
            }

            return rows;
        }
    }

    /// <summary>Whether the card has any line to show at all.</summary>
    public bool HasMountRows => MountRows.Count > 0;

    /// <summary>Who the blueprint says addresses <paramref name="virtualPath"/>, joined for display.</summary>
    private string AgentsOf(string virtualPath) => string.Join(
        ", ",
        _model.DerivedMounts
            .FirstOrDefault(d => string.Equals(d.VirtualPath, virtualPath, StringComparison.Ordinal))
            ?.Agents ?? []);

    /// <summary>
    /// Whether the team will READ a mount that no folder of yours backs — the one case where
    /// a team runs, reports success, and has seen nothing.
    /// <para>
    /// Adoption binds an unclaimed read root to a folder INSIDE the team (<c>input/</c>),
    /// created empty, and nothing ever copies anything into it. The promoted card says so;
    /// the screen that generates the team never did, and its own tasks read «find under
    /// /workspace the folder containing the notes». Saying it here is saying it in time.
    /// </para>
    /// </summary>
    public bool NeedsInputFolder =>
        MountRows.Any(r => r is { HasFolder: false, IsReadWrite: false, IsUnreadable: false });

    /// <summary>
    /// The virtual roots the blueprint addresses and the user dropped anyway. Nothing will be
    /// bound to them at adoption, so the agents told to write there will fail — the screen says
    /// which ones rather than letting the run report success and produce nothing.
    /// </summary>
    public IReadOnlyList<string> DroppedDerivedRoots =>
        [.. _model.DerivedMounts.Select(d => d.VirtualPath).Where(_droppedDerivedRoots.Contains)];

    /// <summary>Whether any agent-addressed root was dropped — drives the warning line.</summary>
    public bool HasDroppedDerivedRoots => DroppedDerivedRoots.Count > 0;

    /// <summary>The warning naming the roots the agents address and nothing will back.</summary>
    public string DroppedDerivedWarning =>
        HasDroppedDerivedRoots
            ? string.Format(
                CultureInfo.CurrentCulture,
                _strings[StudioStringKeys.WizardDroppedDerived],
                string.Join(", ", DroppedDerivedRoots))
            : "";

    /// <summary>« Ajouter un agent ».</summary>
    public RelayCommand AddAgentCommand { get; }

    /// <summary>« Autoriser un dossier » — the shell opens the shared picker.</summary>
    public RelayCommand AllowFolderCommand { get; }

    /// <summary>Retires one mount chip.</summary>
    public RelayCommand RemoveTeamMountCommand { get; }

    /// <summary>
    /// Drops one of the folders the agents imply. The command parameter is the virtual root:
    /// nothing is bound to it at adoption afterwards, and the warning line says so.
    /// </summary>
    public RelayCommand RemoveDerivedMountCommand { get; }

    /// <summary>Puts every dropped agent-addressed root back — the way out of a wrong ✕.</summary>
    public RelayCommand RestoreDerivedMountsCommand { get; }

    /// <summary>
    /// Raised by « Autoriser un dossier » and by a row's « Choisir le dossier… » — the shell
    /// opens the shared chooser, on the named mount point when the request carries one.
    /// </summary>
    public event EventHandler<AllowFolderRequestedEventArgs>? AllowFolderRequested;

    /// <summary>
    /// The gesture that answers ONE mount point: it takes the row's virtual path and comes
    /// back with the folder the user picked, bound to that name.
    /// </summary>
    public RelayCommand BindMountCommand { get; }

    /// <summary>
    /// Binds <paramref name="mount"/>'s folder and rights behind <paramref name="targetVirtualPath"/>,
    /// replacing whatever was there.
    /// <para>
    /// The settings entry is carried over in the sense that matters — its folder and its
    /// RIGHTS — and only the name the agents use for it is the team's to choose. A mount
    /// point takes one folder, so an earlier binding on the same root is removed rather than
    /// added beside: the runtime does not merge two mounts on one root, it drops one.
    /// </para>
    /// </summary>
    /// <param name="targetVirtualPath">The mount point being answered.</param>
    /// <param name="mount">The folder the user picked, as the settings declare it.</param>
    public void BindTeamMount(string targetVirtualPath, MountDefinition mount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetVirtualPath);
        ArgumentNullException.ThrowIfNull(mount);

        foreach (var existing in TeamMounts.ToList())
        {
            if (MountDefinition.TryParse(existing, out var parsed, out _)
                && string.Equals(parsed?.VirtualPath, targetVirtualPath, StringComparison.Ordinal))
            {
                TeamMounts.Remove(existing);
            }
        }

        // Answering a root the blueprint implied un-drops it: the user just said what sits
        // behind it, which is the opposite of dropping it.
        _droppedDerivedRoots.Remove(targetVirtualPath);
        TeamMounts.Add((mount with { VirtualPath = targetVirtualPath }).ToMountString());
        RefreshMountSurfaces();
    }

    /// <summary>
    /// The mount strings the sidecar records: the folders the user allowed, plus the write
    /// roots the blueprint itself addresses, bound to folders inside the team.
    /// <para>
    /// Studio launches an adopted team with its own <c>--mount</c> arguments rather than
    /// through <c>run.sh</c>, so recording only what the user picked left the team without
    /// the very <c>/output</c> its agents were told to write to: the trial passed, the run
    /// then reported success and produced nothing. A folder the user allowed for the same
    /// virtual root wins — an explicit choice beats a derived one.
    /// </para>
    /// </summary>
    /// <summary>
    /// Folder inside an adopted team backing the derived read mount. It is the folder
    /// <c>forge promote</c> creates — Studio adopts by running that very command, so the
    /// name has to match it, and Studio.Wpf cannot reference the CLI to prove it: both
    /// suites assert the literal instead (<c>ForgePromoteTests</c>, <c>CreateTeamWizardTests</c>).
    /// </summary>
    internal const string ReadFolderName = "input";

    internal List<string> WithDerivedWriteMounts(string teamDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamDirectory);

        var mounts = new List<string>(TeamMounts);
        // A root the user dropped stays dropped. Re-adding it here is exactly the silent undo
        // this method was written to stop doing to an explicit choice.
        var claimed = mounts
            .Select(m => MountDefinition.TryParse(m, out var parsed, out _) ? parsed?.VirtualPath : null)
            .Where(virtualPath => virtualPath is not null)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var derived in DerivedMounts)
        {
            if (_droppedDerivedRoots.Contains(derived.VirtualPath) || !claimed.Add(derived.VirtualPath))
                continue;

            // The read mount lands on `input/`, not on the team's root — `forge promote`
            // binds it the same way, and for the same reason: an appsettings.json copied
            // into the team holds API keys, and a read mount over the root would hand them
            // to any agent with a file tool.
            var folder = derived.IsReadWrite ? derived.VirtualPath.TrimStart('/') : ReadFolderName;

            mounts.Add(new MountDefinition
            {
                PhysicalPath = System.IO.Path.Combine(teamDirectory, folder),
                VirtualPath = derived.VirtualPath,
                Rights = derived.IsReadWrite ? MountRights.ReadWrite : MountRights.ReadOnly,
            }.ToMountString());
        }

        return mounts;
    }

    /// <summary>
    /// Republishes every folder surface of the Composer step at once. They are all projections
    /// of the same two lists (the allowed mounts and the dropped roots), so they refresh
    /// together or they disagree.
    /// </summary>
    private void RefreshMountSurfaces()
    {
        RestoreDerivedMountsCommand.RaiseCanExecuteChanged();
        OnPropertiesChanged(
            nameof(HasTeamMounts),
            nameof(MountRows),
            nameof(HasMountRows),
            nameof(NeedsInputFolder),
            nameof(HasUndeclaredTeamMounts),
            nameof(HasDerivedMounts),
            nameof(DroppedDerivedRoots),
            nameof(HasDroppedDerivedRoots),
            nameof(DroppedDerivedWarning));
    }

    /// <summary>Adds the picker's choice to the team's future mounts.</summary>
    public void AddTeamMount(MountDefinition mount)
    {
        ArgumentNullException.ThrowIfNull(mount);
        TeamMounts.Add(mount.ToMountString());
        // Claiming a root un-drops it, whichever gesture claimed it: the banner otherwise
        // went on warning that nothing would be bound to a root the user had just bound.
        _droppedDerivedRoots.Remove(mount.VirtualPath);
        RefreshMountSurfaces();
    }

    /// <summary>
    /// « Modifier » is actionable at the engine's two edit points: while it waits at its
    /// arbitration (the edit decision exists), and at the dry pause of the Composer step
    /// (v3 W-10 — the engine is off, and the apply is a <c>resume --edit</c>). While the
    /// assistant composes or a trial runs, the button waits with the engine.
    /// </summary>
    public bool CanEditAgents =>
        _model.BlueprintJson is not null
        && (_model.DecisionOptions.Contains("edit", StringComparer.Ordinal) || CanTryTeam);

    /// <summary>Opens the agent editor — over an agent, or for a new one when null.</summary>
    private void EditAgent(string? agentKey)
    {
        if (!CanEditAgents || _model.BlueprintJson is not { } blueprintJson)
            return;

        AgentEditor.Open(blueprintJson, agentKey, [.. TeamMounts], amended =>
        {
            // The engine owns the truth: at the arbitration the decision goes first, the
            // amended blueprint follows, and everything is re-validated on its side of
            // the wire. A dead engine must be SAID — closing the editor as if applied
            // would lose the edit.
            if (_model.DecisionOptions.Contains("edit", StringComparer.Ordinal))
            {
                if (!_client.SendDecision("edit") || !_client.SendBlueprint(amended))
                {
                    StatusMessage = _strings[StudioStringKeys.WizardAssistantNotRunning];
                    return;
                }

                _model.AcknowledgeDecision();
                SyncFromModel();
                return;
            }

            // At the dry pause the engine is off (W-10): a `resume --edit` carries the
            // amended blueprint as the child's first stdin line, re-renders
            // deterministically — zero LLM tokens, same iteration — and pauses again at
            // the same boundary, so the Composer repaints with the amended team.
            if (CanTryTeam && _model.Slug is { } slug)
            {
                _ = EditAtPauseAsync(slug, amended);
                return;
            }

            StatusMessage = _strings[StudioStringKeys.WizardAssistantNotRunning];
        });
    }

    /// <summary>The dry-pause edit's engine run; the race with another launch is said, never thrown.</summary>
    private async Task EditAtPauseAsync(string slug, string amendedBlueprintJson)
    {
        try
        {
            await RunEngineAsync(new ForgeStartRequest
            {
                ResumeSlug = slug,
                WorkingDirectory = _workspace,
                Dry = true,
                EditedBlueprintJson = amendedBlueprintJson,
                EnvironmentOverrides = AssistantEnvironment(),
            }).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            _dispatcher.Post(() => StatusMessage = _strings[StudioStringKeys.WizardAssistantNotRunning]);
        }
    }

    /// <summary>The proposal's plain-words rationale.</summary>
    public string? Rationale => _model.Proposal?.Rationale;

    /// <summary>What the team may do, in words the user can read.</summary>
    public IReadOnlyList<WizardToolChip> Tools =>
        [.. (_model.Proposal?.Tools ?? [])
            .Select(tool => WizardToolChips.For(tool, _strings, ReadScope(), WriteScope()))];

    /// <summary>
    /// The mount a reading tool addresses, taken from what the blueprint actually implies.
    /// Null when the blueprint implies none — better a chip that says it reads files than
    /// one that names a folder nobody chose.
    /// </summary>
    private string? ReadScope() =>
        _model.DerivedMounts.FirstOrDefault(m => !m.IsReadWrite)?.VirtualPath;

    /// <summary>The mount a writing tool addresses, same rule.</summary>
    private string? WriteScope() =>
        _model.DerivedMounts.FirstOrDefault(m => m.IsReadWrite)?.VirtualPath;

    /// <summary>Whether the tools row shows.</summary>
    public bool HasTools => Tools.Count > 0;

    /// <summary>
    /// Whether any bound folder really sits outside the authorized ones — the only thing the
    /// red legend can honestly be about. It used to be shown unconditionally, beside chips
    /// that carry no physical path at all and were therefore never compared to anything.
    /// </summary>
    public bool HasUndeclaredTeamMounts => MountRows.Any(r => r.IsUndeclared);

    /// <summary>
    /// Whether the engine has actually proposed a team. The proposal card used to render
    /// unconditionally, so between «entered the blueprint stage» and «blueprint.ready» the
    /// screen showed a confident here-is-the-team-I-propose headline over nothing at all — and the
    /// only sentence with substance in it was a red warning about unauthorized folders.
    /// </summary>
    public bool HasProposal => _model.Proposal is not null;

    /// <summary>Whether the render produced anything to show under the generated-definition heading.</summary>
    public bool HasGeneratedDefinition => HasCrewDefinition || HasFiles;

    /// <summary>Crew files written by the render, session-relative — expert only.</summary>
    public IReadOnlyList<string> Files => _model.Files;

    /// <summary>Whether files were rendered.</summary>
    public bool HasFiles => _model.Files.Count > 0;

    /// <summary>Last validation outcome; null before the first one.</summary>
    public bool? ValidationOk => _model.ValidationOk;

    /// <summary>The validator's sentences, verbatim.</summary>
    public IReadOnlyList<string> ValidationErrors => _model.ValidationErrors;

    // ── step 3 : Essayer ──

    /// <summary>Whether a try is currently running.</summary>
    public bool RunInProgress => _model.RunInProgress;

    /// <summary>The try's activity, in plain language, completion order.</summary>
    public ObservableCollection<WizardActivityLine> Activity { get; } = [];

    /// <summary>1-based number of the running (or last) try.</summary>
    public int Attempt => _model.RunNumber ?? _model.Iteration;

    /// <summary>The verdict, once the judge spoke.</summary>
    public ForgeVerdictView? Verdict => _model.Verdict;

    /// <summary>Whether the verdict card shows.</summary>
    public bool HasVerdict => _model.Verdict is not null;

    /// <summary>The ✔/✘ checklist against the user's own criteria.</summary>
    public ObservableCollection<WizardChecklistLine> Checklist { get; } = [];

    /// <summary>
    /// What the engine suggests changing. It computes these, puts them on the wire and
    /// Studio.Core parses them — and nothing displayed them, so the fix-and-retry button
    /// asked the user to invent a correction the engine had already written.
    /// </summary>
    public IReadOnlyList<string> Suggestions =>
    [
        .. (_model.Verdict?.Suggestions ?? [])
            .Select(s => string.Join(" — ", new[] { s.Target, s.Change, s.Reason }
                .Where(part => part is { Length: > 0 })))
            .Where(line => line.Length > 0),
    ];

    /// <summary>Whether the engine had anything to suggest.</summary>
    public bool HasSuggestions => Suggestions.Count > 0;

    /// <summary>
    /// Whether the verdict came from the deterministic fallback rather than an LLM judge.
    /// <para>
    /// It matters because of what the checklist then looks like: with no LLM judge there is
    /// no per-criterion verdict, so every line reads «?» — honest, and completely opaque
    /// unless the screen says why. The score is a constant in that case too.
    /// </para>
    /// </summary>
    public bool VerdictIsMechanical =>
        _model.Verdict is { } v && !string.Equals(v.Judge, ForgeVerdictView.JudgeLlm, StringComparison.Ordinal);

    /// <summary>"score 0,78" under the verdict title.</summary>
    public string VerdictScore =>
        _model.Verdict is { } verdict
            ? string.Create(CultureInfo.CurrentCulture, $"score {verdict.Score:0.00}")
            : "";

    /// <summary>Whether the verdict passed.</summary>
    public bool VerdictPassing => _model.Verdict?.Passing ?? false;

    /// <summary>
    /// The trial's own cost as mono chips under the verdict title (v3 W-08):
    /// «12 840 tokens» · «cache 62 % · 7 980 tokens» · «59 s». Empty when the engine
    /// did not measure — no chip is ever a fabricated zero.
    /// </summary>
    public IReadOnlyList<string> VerdictMetricChips =>
        _model.Verdict is { } verdict
            ? UsageMetricsFormatter.Chips(
                verdict.Tokens, verdict.CacheHitTokens, verdict.CacheMissTokens, verdict.DurationMs,
                _strings, CultureInfo.CurrentCulture)
            : [];

    /// <summary>Whether any metric chip exists to show.</summary>
    public bool HasVerdictMetrics => VerdictMetricChips.Count > 0;

    /// <summary>Cumulative tokens spent by the session.</summary>
    public long TokensSpent => _model.TokensSpent;

    // ── step 4 : Adopter ──

    /// <summary>The team's name; prefilled from the brief's goal.</summary>
    public string TeamName
    {
        get => _teamName;
        set
        {
            if (SetProperty(ref _teamName, value))
            {
                OnPropertyChanged(nameof(CanSaveTeam));
                SaveTeamCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>0 = on demand, 1 = daily, 2 = hourly — the engine's own schedule grammar.</summary>
    public int ScheduleChoice
    {
        get => _scheduleChoice;
        set
        {
            if (SetProperty(ref _scheduleChoice, value))
            {
                // CanSaveTeam tests the time only on the daily choice: leaving "daily" with
                // an invalid time must wake the save button up again.
                OnPropertiesChanged(nameof(NeedsScheduleTime), nameof(IsOnDemand), nameof(IsDaily), nameof(IsHourly), nameof(CanSaveTeam));
                SaveTeamCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Whether the time field shows (daily only).</summary>
    public bool NeedsScheduleTime => _scheduleChoice == 1;

    /// <summary>Radio surface of <see cref="ScheduleChoice"/>: on demand.</summary>
    public bool IsOnDemand
    {
        get => _scheduleChoice == 0;
        set { if (value) ScheduleChoice = 0; }
    }

    /// <summary>Radio surface of <see cref="ScheduleChoice"/>: daily.</summary>
    public bool IsDaily
    {
        get => _scheduleChoice == 1;
        set { if (value) ScheduleChoice = 1; }
    }

    /// <summary>Radio surface of <see cref="ScheduleChoice"/>: hourly.</summary>
    public bool IsHourly
    {
        get => _scheduleChoice == 2;
        set { if (value) ScheduleChoice = 2; }
    }

    /// <summary>Local time of the daily schedule, HH:mm.</summary>
    public string ScheduleTime
    {
        get => _scheduleTime;
        set
        {
            if (SetProperty(ref _scheduleTime, value))
            {
                OnPropertyChanged(nameof(CanSaveTeam));
                SaveTeamCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Name of the profile the team will run on; the machine default when null.</summary>
    public string? AdoptProfileName
    {
        get => _adoptProfileName ?? Profiles.DefaultProfileName;
        set
        {
            if (SetProperty(ref _adoptProfileName, value))
                OnPropertyChanged(nameof(AdoptProfileSummary));
        }
    }

    /// <summary>
    /// A goal-length title cut to a display name (v3 W-07): the engine now demands a
    /// short <c>crew.name</c>, but an older session's title may still be the goal
    /// sentence — trim at a word boundary, never mid-word.
    /// </summary>
    internal static string ShortName(string title)
    {
        const int MaxLength = 48;
        var trimmed = title.Trim();
        if (trimmed.Length <= MaxLength)
            return trimmed;

        var cut = trimmed.LastIndexOf(' ', MaxLength);
        return (cut > 0 ? trimmed[..cut] : trimmed[..MaxLength]).TrimEnd(',', ';', ':', '.');
    }

    /// <summary>The selected profile's one-line origin, for the step-4 card (v3 W-07).</summary>
    public string AdoptProfileSummary =>
        Profiles.Profiles.FirstOrDefault(p => string.Equals(p.Name, AdoptProfileName, StringComparison.Ordinal))
            ?.Summary ?? "";

    /// <summary>Whether the «Changer» rows of the step-4 profile card are unfolded.</summary>
    public bool IsAdoptProfilePickerOpen
    {
        get => _isAdoptProfilePickerOpen;
        set => SetProperty(ref _isAdoptProfilePickerOpen, value);
    }

    /// <summary>«Changer» — unfolds/folds the profile rows.</summary>
    public RelayCommand ToggleAdoptProfilePickerCommand { get; }

    /// <summary>Picks the team's profile from the unfolded rows and folds them back.</summary>
    public RelayCommand PickAdoptProfileCommand { get; }

    /// <summary>True once the team landed in the teams folder.</summary>
    public bool IsSaved
    {
        get => _isSaved;
        private set
        {
            if (SetProperty(ref _isSaved, value))
            {
                OnPropertyChanged(nameof(NotSaved));
                RaiseDraftChanged();
                ReopenComposeCommand?.RaiseCanExecuteChanged();
                RetryTrialCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>The pre-save card's visibility.</summary>
    public bool NotSaved => !_isSaved;

    /// <summary>The engine invocation of the running (or last) cycle — expert step 3.</summary>
    public string? EngineCommandLine { get; private set; }

    /// <summary>Where the team landed.</summary>
    public string? SavedPath => _model.Promotion?.Path;

    /// <summary>The scheduling command — displayed, never executed by Studio.</summary>
    public string? InstallCommand => _model.Promotion?.Install;

    /// <summary>Whether an install command exists to show.</summary>
    public bool HasInstallCommand => _model.Promotion?.Install is { Length: > 0 };

    /// <summary>Whether the "save to my teams" command may run.</summary>
    public bool CanSaveTeam =>
        !IsEngineRunning
        && !IsSaved
        && _model.Slug is not null
        && _teamName.Trim().Length > 0
        && (_scheduleChoice != 1 || IsValidScheduleTime(_scheduleTime))
        && (_model.Stage is "ready" || string.Equals(_model.FinishedStatus, "ready", StringComparison.Ordinal));

    /// <summary>The engine's daily grammar is <c>daily@HH:mm</c> — a time it would refuse never leaves Studio.</summary>
    private static bool IsValidScheduleTime(string time) =>
        TimeSpan.TryParseExact(time.Trim(), @"h\:mm", CultureInfo.InvariantCulture, out _)
        || TimeSpan.TryParseExact(time.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out _);

    /// <summary>The "try the team" button — the trial, as an explicit click at the Compose pause.</summary>
    public AsyncRelayCommand TryTeamCommand { get; }

    /// <summary>
    /// The other answer to the same pause: keep the team as generated, without a trial.
    /// <para>
    /// It is offered beside the trial rather than instead of it, and its label says what it
    /// skips. Nothing downstream needs the trial — the crew is rendered and validated here,
    /// and the promoted card already knows how to say «no verdict recorded» — so the trial
    /// was buying evidence, not permission. A user who does not want the evidence is
    /// entitled to say so, once, in words.
    /// </para>
    /// </summary>
    public AsyncRelayCommand AdoptWithoutTrialCommand { get; }

    /// <summary>Promotes the session into the teams folder and writes the Studio sidecar.</summary>
    public AsyncRelayCommand SaveTeamCommand { get; }

    /// <summary>Jumps to the settings screen (the gate's "manage settings" button).</summary>
    public RelayCommand OpenSettingsCommand { get; }

    /// <summary>The gate's quick pick: elects the named profile as the assistant's.</summary>
    public RelayCommand PickAssistantCommand { get; }

    // ── gestures ──

    /// <summary>Resumes a stopped session where it left off.</summary>
    public async Task ResumeAsync(ForgeSolutionSummary solution)
    {
        ArgumentNullException.ThrowIfNull(solution);
        if (IsEngineRunning)
            return;

        ResetProjection();
        ForgeSessionHydrator.Hydrate(_model, solution.Directory);
        SessionActivated?.Invoke(this, EventArgs.Empty);
        SyncFromModel();

        // A session parked at the --dry pause (saved state: Test, not yet run) reopens on
        // the Composer review — running the trial stays the user's click, on a resume as
        // on a fresh compose. Every other state genuinely needs the engine back.
        if (string.Equals(solution.State, "Test", StringComparison.OrdinalIgnoreCase))
        {
            _model.MarkPaused();
            SyncFromModel();
            return;
        }

        await RunEngineAsync(new ForgeStartRequest
        {
            ResumeSlug = solution.Slug,
            WorkingDirectory = _workspace,
            EnvironmentOverrides = AssistantEnvironment(),
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// «Modifier» on a team card (v3 W-09): reopens the wizard on the adopted team — the
    /// session re-enters at its arbitration (the engine's reopen), the wizard shows step
    /// 2 with the whole stepper reachable, and the adoption fields are seeded from the
    /// sidecar. Re-adoption then updates the SAME folder: the destination is pinned,
    /// renaming only changes the display name.
    /// </summary>
    public async Task ReopenTeamAsync(TeamSummary team, ForgeSolutionSummary session)
    {
        ArgumentNullException.ThrowIfNull(team);
        ArgumentNullException.ThrowIfNull(session);
        if (IsEngineRunning)
            return;

        ResetProjection();
        _reopenedTeamPath = team.Path;
        ForgeSessionHydrator.Hydrate(_model, session.Directory);

        TeamName = team.Name;
        if (team.Profile is { Length: > 0 } profile)
            AdoptProfileName = profile;
        SeedSchedule(team.Schedule);
        foreach (var mount in team.Mounts)
            TeamMounts.Add(mount);
        RefreshMountSurfaces();

        MaxStep = 4;
        Step = 2;
        SessionActivated?.Invoke(this, EventArgs.Empty);
        SyncFromModel();

        await RunEngineAsync(new ForgeStartRequest
        {
            ResumeSlug = session.Slug,
            WorkingDirectory = _workspace,
            EnvironmentOverrides = AssistantEnvironment(),
        }).ConfigureAwait(false);
    }

    /// <summary>The "edit the team" button on the saved card — back to step 2, state intact.</summary>
    public AsyncRelayCommand ReopenComposeCommand { get; }

    /// <summary>The "run another trial" button on the saved card — back to step 3, the trial re-runs.</summary>
    public AsyncRelayCommand RetryTrialCommand { get; }

    /// <summary>
    /// Reopens the just-adopted session (the engine's reopen re-enters at the
    /// arbitration); <paramref name="autoRetry"/> answers it with <c>retry</c> the moment
    /// it arrives — "Run the trial again" means the trial runs, not "go find a button".
    /// </summary>
    private async Task ReopenAdoptedAsync(int step, bool autoRetry)
    {
        if (IsEngineRunning || _model.Slug is not { } slug || SavedPath is not { } savedPath)
            return;

        _reopenedTeamPath = savedPath;
        _autoRetryPending = autoRetry;
        IsSaved = false;
        MaxStep = 4;
        Step = step;
        SyncFromModel();

        await RunEngineAsync(new ForgeStartRequest
        {
            ResumeSlug = slug,
            WorkingDirectory = _workspace,
            EnvironmentOverrides = AssistantEnvironment(),
        }).ConfigureAwait(false);
    }

    /// <summary>Projects the sidecar's schedule string back onto the step-4 radios.</summary>
    private void SeedSchedule(string? schedule)
    {
        if (string.IsNullOrWhiteSpace(schedule))
        {
            ScheduleChoice = 0;
            return;
        }

        if (schedule.StartsWith("daily@", StringComparison.OrdinalIgnoreCase))
        {
            ScheduleChoice = 1;
            ScheduleTime = schedule["daily@".Length..];
        }
        else if (string.Equals(schedule, "hourly", StringComparison.OrdinalIgnoreCase))
        {
            ScheduleChoice = 2;
        }
        else
        {
            ScheduleChoice = 0;
        }
    }

    private async Task ComposeAsync()
    {
        if (!CanCompose)
            return;

        ResetProjection();

        // The thread takes the column for the whole brief stage: every question from here
        // is the model's, and the answers go back down the same pipe.
        Chat.StartSession();

        var brief = ComposeBrief();
        _model.AddUserMessage(brief);
        SessionActivated?.Invoke(this, EventArgs.Empty);
        SyncFromModel();

        await RunEngineAsync(new ForgeStartRequest
        {
            Need = brief,
            WorkingDirectory = _workspace,
            EnvironmentOverrides = AssistantEnvironment(),
            // The Composer pause (owner, 2026-08-24): generate and validate, then STOP.
            // The trial is the user's click on "try the team", never a side effect
            // of composing — the engine's --dry boundary is exactly this.
            Dry = true,
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// The "try the team" gesture — resumes the paused session without --dry: the engine picks
    /// up exactly at the trial, then waits at its arbitration.
    /// </summary>
    private async Task TryTeamAsync()
    {
        if (!CanTryTeam || _model.Slug is not { } slug)
            return;

        await RunEngineAsync(new ForgeStartRequest
        {
            ResumeSlug = slug,
            WorkingDirectory = _workspace,
            EnvironmentOverrides = AssistantEnvironment(),
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// The "adopt as generated" gesture — resumes the paused session with --adopt: the
    /// engine moves it to Ready and exits, offline, without a run and without a token.
    /// </summary>
    private async Task AdoptWithoutTrialAsync()
    {
        if (!CanTryTeam || _model.Slug is not { } slug)
            return;

        await RunEngineAsync(new ForgeStartRequest
        {
            ResumeSlug = slug,
            WorkingDirectory = _workspace,
            Adopt = true,
            EnvironmentOverrides = AssistantEnvironment(),
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// The trial can start when the session sits at the --dry pause: composed, rendered,
    /// validated, nothing run yet.
    /// </summary>
    public bool CanTryTeam =>
        !IsEngineRunning
        && !IsSaved
        && _model.Slug is not null
        && string.Equals(_model.FinishedStatus, "paused", StringComparison.OrdinalIgnoreCase);

    private string ComposeBrief()
    {
        var lines = new List<string> { _need.Trim() };

        if (FrequencyChoices.FirstOrDefault(c => c.IsSelected) is { } freq)
            lines.Add(string.Format(CultureInfo.CurrentCulture, _strings[StudioStringKeys.WizardBriefFrequency], freq.Label));
        if (SourceChoices.FirstOrDefault(c => c.IsSelected) is { } source)
            lines.Add(string.Format(CultureInfo.CurrentCulture, _strings[StudioStringKeys.WizardBriefSource], source.Label));
        if (OutputChoices.FirstOrDefault(c => c.IsSelected) is { } output)
            lines.Add(string.Format(CultureInfo.CurrentCulture, _strings[StudioStringKeys.WizardBriefOutput], output.Label));
        if (_outcome.Trim() is { Length: > 0 } outcome)
            lines.Add(string.Format(CultureInfo.CurrentCulture, _strings[StudioStringKeys.WizardBriefShape], outcome));
        // Nothing is spliced in here any more: what the model still needs it asks for
        // itself during the brief stage, and the answers reach it as real user messages.
        if (ComposeNotes.Consigne.Trim() is { Length: > 0 } consigne)
            lines.Add(string.Format(CultureInfo.CurrentCulture, _strings[StudioStringKeys.WizardBriefConsigne], consigne));

        return string.Join(" ", lines);
    }

    private IReadOnlyDictionary<string, string> AssistantEnvironment() =>
        Profiles.Set.Studio?.EnvironmentOverrides(Environment.GetEnvironmentVariable)
        ?? new Dictionary<string, string>(StringComparer.Ordinal);

    private async Task RunEngineAsync(ForgeStartRequest request)
    {
        // OnEvent and OnRaw already discard anything from a superseded run; the finally
        // below did not, so a dying child could reach back and switch off the run that
        // replaced it. Harmless while it only cleared a flag — not once it can push a
        // farewell into a conversation that has already started over.
        var generation = _runGeneration;

        IsEngineRunning = true;
        _lastStderr = null;
        // « COMMANDE DE L'ESSAI » (F-09, expert): the engine invocation, replayable in a
        // terminal — the honest equivalent of a command preview for a forge-driven trial.
        EngineCommandLine = Orkeon.Studio.Core.Launch.CommandLineDisplay.Format(
            ForgeArgumentsBuilder.Build(request));
        OnPropertyChanged(nameof(EngineCommandLine));
        try
        {
            var result = await _client.RunAsync(request, OnEvent, OnRaw, CancellationToken.None).ConfigureAwait(false);
            _dispatcher.Post(() => FinishRun(result));
        }
        finally
        {
            _dispatcher.Post(() =>
            {
                if (generation != _runGeneration)
                    return;

                IsEngineRunning = false;

                // Gone before the brief was accepted: a crash, a non-zero exit, a missing
                // binary, a Stop. The thread would otherwise keep showing a question with a
                // composer writing into a closed pipe.
                Chat.EngineFinished(interrupted: Chat.IsStarted && !Chat.IsDone);
                // An unconsumed auto-retry must die with its run: a crashed or stopped
                // engine must never leave a pending decision to fire on a later one.
                _autoRetryPending = false;
                SyncFromModel();
            });
        }
    }

    private async Task SaveTeamAsync()
    {
        if (!CanSaveTeam || _model.Slug is not { } slug)
            return;

        // In reopened mode the destination is PINNED to the original team folder (W-09):
        // re-adoption updates, never duplicates — renaming only changes the display name.
        var destination = _reopenedTeamPath
            ?? System.IO.Path.Combine(_teamsRoot, TeamCatalog.Slugify(_teamName));
        var schedule = _scheduleChoice switch
        {
            1 => $"daily@{_scheduleTime.Trim()}",
            2 => "hourly",
            _ => null,
        };

        IsEngineRunning = true;
        _lastStderr = null;
        _saveError = null;
        try
        {
            var result = await _client.PromoteAsync(slug, destination, schedule, _workspace, OnEvent, OnRaw)
                .ConfigureAwait(false);
            _dispatcher.Post(() =>
            {
                FinishRun(result);
                if (_model.Promotion is { } promotion)
                {
                    // A re-adoption has no step-1 need: the sidecar's description must
                    // survive the rewrite, not be blanked by it.
                    var description = _need.Trim() is { Length: > 0 } need
                        ? need
                        : TeamCatalog.Describe(promotion.Path).Description ?? "";
                    var mounts = WithDerivedWriteMounts(promotion.Path);
                    TeamCatalog.SaveMetadata(promotion.Path, new StudioTeamMetadata
                    {
                        Name = _teamName.Trim(),
                        Description = description,
                        Profile = AdoptProfileName,
                        Schedule = schedule,
                        Mounts = mounts.Count > 0 ? mounts : null,
                    });
                    IsSaved = true;
                    TeamAdopted?.Invoke(this, new TeamAdoptedEventArgs(promotion.Path));
                }
                else
                {
                    // No promoted event means no team on disk — a silent button would read
                    // as success, so the refusal is said out loud with what the engine said.
                    // Held in a field: the ordinary sync would repaint "ready" over it.
                    _saveError = string.Format(
                        CultureInfo.CurrentCulture,
                        _strings[StudioStringKeys.WizardPromoteFailed],
                        _lastStderr ?? string.Create(CultureInfo.InvariantCulture, $"exit {result.ExitCode}"));
                }
            });
        }
        finally
        {
            _dispatcher.Post(() =>
            {
                IsEngineRunning = false;
                SyncFromModel();
            });
        }
    }

    private void Restart()
    {
        _client.RequestCancellation();
        ResetProjection();
        // The conversation belongs to the creation being abandoned — unlike ResetProjection,
        // which also runs at the START of a compose and must leave the interview's answers
        // exactly where the interview put them.
        Chat.Reset();
        Need = "";
        Outcome = "";
        foreach (var choice in FrequencyChoices.Concat(SourceChoices).Concat(OutputChoices))
            choice.IsSelected = false;
        ComposeNotes.Consigne = "";
        TryNotes.Consigne = "";
        AdoptNotes.Consigne = "";

        SyncFromModel();
    }

    private void ResetProjection()
    {
        // A new session starts from a clean slate: the previous team's folders were
        // approved for THAT team, never for the next one; the old engine command lies. The
        // dropped roots go with them — they were dropped from the previous blueprint.
        TeamMounts.Clear();
        _droppedDerivedRoots.Clear();
        RefreshMountSurfaces();
        _reopenedTeamPath = null;
        _autoRetryPending = false;
        EngineCommandLine = null;
        OnPropertyChanged(nameof(EngineCommandLine));
        // The generation bump orphans every event the dying child still has in flight:
        // a straggler posted before the swap must not repopulate the fresh model.
        _runGeneration++;
        _model = new ForgeSessionModel();
        _saveError = null;
        RawLog.Clear();
        Activity.Clear();
        Checklist.Clear();
        Agents.Clear();
        Decisions.Clear();
        IsSaved = false;
        TeamName = "";
        // The adoption fields too (review, lot 7): a reopened team seeds profile and
        // schedule — without this reset they would leak into the NEXT session's sidecar.
        _adoptProfileName = null;
        OnPropertiesChanged(nameof(AdoptProfileName), nameof(AdoptProfileSummary));
        ScheduleChoice = 0;
        ScheduleTime = "07:30";
        IsAdoptProfilePickerOpen = false;
        StatusMessage = "";
        Step = 1;
        MaxStep = 1;
    }

    /// <summary>
    /// "What I've noted". The brief and the three step-1 precisions first, then one
    /// row per interview question, then each instruction that was actually typed — an
    /// empty instruction is not a fact the assistant is missing, so it is not listed.
    /// </summary>
    private IReadOnlyList<ChatRecapFact> BuildRecapFacts()
    {
        var pending = _strings[StudioStringKeys.ChatFactPending];
        var facts = new List<ChatRecapFact>
        {
            Fact(StudioStringKeys.ChatFactBrief, Clip(_need.Trim(), 72)),
            Fact(StudioStringKeys.ChatFactRhythm, FrequencyChoices.FirstOrDefault(c => c.IsSelected)?.Label),
            Fact(StudioStringKeys.ChatFactSource, SourceChoices.FirstOrDefault(c => c.IsSelected)?.Label),
            Fact(StudioStringKeys.ChatFactOutput, OutputChoices.FirstOrDefault(c => c.IsSelected)?.Label),
        };

        foreach (var (key, note) in new[]
        {
            (StudioStringKeys.ChatFactComposeNote, ComposeNotes.Consigne),
            (StudioStringKeys.ChatFactTryNote, TryNotes.Consigne),
            (StudioStringKeys.ChatFactAdoptNote, AdoptNotes.Consigne),
        })
        {
            if (note.Trim() is { Length: > 0 } typed)
                facts.Add(new ChatRecapFact(_strings[key], Clip(typed, 72), IsKnown: true));
        }

        return facts;

        ChatRecapFact Fact(string key, string? value) => new(
            _strings[key],
            value is { Length: > 0 } known ? known : pending,
            value is { Length: > 0 });
    }

    private string ChatMessageCount() => Chat.Turns.Count == 0
        ? ""
        : string.Format(
            CultureInfo.CurrentCulture,
            _strings[StudioStringKeys.ChatStatusMessagesPattern],
            Chat.Turns.Count);

    private static string Clip(string text, int max) =>
        text.Length > max ? string.Concat(text.AsSpan(0, max).TrimEnd(), "…") : text;

    private void Decide(string value)
    {
        // "Fix and retry" carries the trial consigne with it: the correction in the user's
        // words travels first, then the arbitration the engine is waiting for.
        if (string.Equals(value, "refine", StringComparison.Ordinal)
            && TryNotes.Consigne.Trim() is { Length: > 0 } consigne)
        {
            _client.SendMessage(consigne);
            _model.AddUserMessage(consigne);
        }

        _client.SendDecision(value);
        // The stream never echoes decision.made back: retire the buttons ourselves so the
        // arbitration cannot be double-sent while the engine works toward its next stage.
        _model.AcknowledgeDecision();
        SyncFromModel();
    }

    private void OnEvent(OrkeonEvent orkeonEvent)
    {
        var generation = _runGeneration;
        _dispatcher.Post(() =>
        {
            if (generation != _runGeneration)
                return;

            var beforeMessages = _model.Messages.Count;
            _model.Feed(orkeonEvent);
            RawLog.AppendNotice(orkeonEvent.Root.GetRawText());

            // The model submitted a brief: the interview is over, and the thread says so
            // before handing the column back to the wizard.
            if (orkeonEvent.Kind == ForgeEventKinds.BriefReady)
                Chat.BriefAccepted();

            // The assistant's turn lands in the thread, and only there. It used to be
            // copied into a bar above the form as well — a bar with no gate on it, so it
            // was the only surface the user ever saw the question on.
            if (_model.Messages.Count > beforeMessages
                && _model.Messages[^1] is { Role: ForgeChatMessage.Assistant, Text: { } text })
            {
                Chat.AddAssistantTurn(text);
            }

            Chat.OwnerChanged();
            SyncFromModel();
        });
    }

    private void OnRaw(ProcessOutputLine line)
    {
        var generation = _runGeneration;
        _dispatcher.Post(() =>
        {
            if (generation != _runGeneration)
                return;

            if (line.Channel == ProcessOutputChannel.StandardError && !string.IsNullOrWhiteSpace(line.Text))
                _lastStderr = line.Text;
            RawLog.Append(line);
        });
    }

    private void FinishRun(ProcessRunResult result)
    {
        if (result.Outcome == RunOutcome.NotStarted)
        {
            StatusMessage = result.Description;
            return;
        }

        // Not gated on FinishedStatus being null any more: a session that reported «failed»
        // still leaves its reason on stderr, and that reason outranks the generic sentence.
        if (result.ExitCode != 0 && _lastStderr is { } stderr)
            StatusMessage = stderr;
    }

    private void GoStep(int step)
    {
        if (step <= MaxStep)
            Step = step;
    }

    private ForgeErrorInfo? _surfacedError;

    private void SyncFromModel()
    {
        // A fresh engine error must reach the status line (review D3): a refused blueprint
        // edit (FORGE-BLUEPRINT-INVALID) is otherwise invisible in novice mode — the editor
        // closes and nothing says why nothing changed. Recoverable OR NOT: the unrecoverable
        // ones used to be dropped on the floor, which is the half the user never saw.
        if (_model.LastError is { } error && !ReferenceEquals(error, _surfacedError))
        {
            _surfacedError = error;
            StatusMessage = error.Message is { Length: > 0 } ? $"{error.Code}: {error.Message}" : error.Code;
        }

        // The milestone drives the stepper; the user may look back, never skip ahead.
        var reached = _model.Milestone switch
        {
            ForgeMilestone.Describe => 1,
            ForgeMilestone.Propose => 2,
            ForgeMilestone.Try => 3,
            _ => 4,
        };
        if (reached > MaxStep)
        {
            MaxStep = reached;
            Step = reached;
        }

        SyncAgents();
        SyncActivity();
        SyncChecklist();
        SyncDecisions();
        SyncProgress();

        // "Run the trial again" answers the reopened arbitration itself (W-09): the flag is
        // cleared BEFORE deciding — Decide re-enters this sync.
        if (_autoRetryPending && DecisionPending
            && _model.DecisionOptions.Contains("retry", StringComparer.Ordinal))
        {
            _autoRetryPending = false;
            Decide("retry");
            return;
        }

        if (_teamName.Length == 0 && _model.Title is { Length: > 0 } title)
            TeamName = ShortName(title);

        // The generic something-went-wrong line must not replace the sentence that says WHAT.
        // A failed session used to overwrite the engine's own error — the one line with
        // enough in it to act on — with a generic apology.
        StatusMessage = _saveError ?? _model.FinishedStatus switch
        {
            "ready" => _strings[StudioStringKeys.ForgeStatusReady],
            "failed" when _model.LastError is null && _lastStderr is null
                => _strings[StudioStringKeys.ForgeStatusFailed],
            "abandoned" or "paused" => _strings[StudioStringKeys.ForgeStatusStopped],
            _ => StatusMessage,
        };

        // The mount surfaces move with the blueprint too: a new set of derived roots arrives
        // with every blueprint.ready, and after a refine the dropped-roots banner goes stale
        // for the same reason. This used to be raised only by user gestures, so the chips
        // appeared when the user happened to touch something else — while HasDerivedMounts,
        // being in the batch below, re-evaluated and showed the hint that explains them.
        RefreshMountSurfaces();

        OnPropertiesChanged(
            nameof(Rationale), nameof(Tools), nameof(HasTools), nameof(HasProposal),
            nameof(HasGeneratedDefinition), nameof(HasUndeclaredTeamMounts),
            nameof(DerivedMounts),
            nameof(Files), nameof(HasFiles), nameof(ValidationOk), nameof(ValidationErrors),
            nameof(RunInProgress), nameof(Attempt), nameof(Verdict), nameof(HasVerdict),
            nameof(VerdictScore), nameof(VerdictPassing), nameof(TokensSpent),
            nameof(VerdictMetricChips), nameof(HasVerdictMetrics),
            nameof(Suggestions), nameof(HasSuggestions), nameof(VerdictIsMechanical),
            nameof(SessionSlug), nameof(SessionDirectory),
            nameof(EngineVersion), nameof(HasEngineVersion), nameof(EngineLabel),
            nameof(CrewDefinitionYaml), nameof(HasCrewDefinition),
            nameof(SavedPath), nameof(InstallCommand), nameof(HasInstallCommand),
            nameof(CanSaveTeam), nameof(DecisionPending), nameof(CanEditAgents), nameof(CanTryTeam),
            nameof(IsEngineWaitingOnUser), nameof(IsEngineWorking));
        SaveTeamCommand.RaiseCanExecuteChanged();
        AddAgentCommand.RaiseCanExecuteChanged();
        TryTeamCommand.RaiseCanExecuteChanged();
        AdoptWithoutTrialCommand.RaiseCanExecuteChanged();
    }

    private void SyncAgents()
    {
        Agents.Clear();
        if (_model.Proposal is not { } proposal)
            return;

        if (proposal.Agents.Count > 0)
        {
            // The blueprint speaks for itself: one card per agent, its own goal and tools,
            // and « Modifier » wired to the edit arbitration.
            foreach (var agent in proposal.Agents)
            {
                Agents.Add(new WizardAgentCard(
                    agent.Key,
                    agent.Role,
                    agent.Role.Length > 0 ? agent.Role[..1].ToUpperInvariant() : "?",
                    agent.Goal is { Length: > 0 } goal
                        ? goal
                        : string.Join(" ", proposal.Steps.Where(step => step.AgentRole == agent.Role).Select(step => step.Description)),
                    // Scoped to the mounts the blueprint really implies, not to a constant.
                    [.. agent.Tools.Select(tool => WizardToolChips.For(tool, _strings, ReadScope(), WriteScope()))],
                    EditAgent,
                    () => CanEditAgents));
            }

            return;
        }

        var fallback = _strings[StudioStringKeys.WizardAgentFallback];
        foreach (var group in proposal.Steps.GroupBy(s => s.AgentRole ?? fallback))
        {
            var name = group.Key;
            Agents.Add(new WizardAgentCard(
                key: null,
                name,
                name.Length > 0 ? name[..1].ToUpperInvariant() : "?",
                string.Join(" ", group.Select(s => s.Description)),
                tools: [],
                edit: null,
                canEdit: static () => false));
        }
    }

    /// <summary>
    /// Feeds the progress card. Everything it shows is already on the stream — the card
    /// exists because none of it was reaching the screen between two milestones.
    /// </summary>
    private void SyncProgress() => Progress.Update(new ComposeProgress(
        _model.Stage,
        IsEngineRunning,
        IsEngineWaitingOnUser,
        _model.FinishedStatus is not null,
        // The model's own last words. A user turn is not narration — it is what the user
        // just typed, and echoing it back as «what the engine is saying» would be a lie.
        _model.Messages.LastOrDefault(m => m.Role == ForgeChatMessage.Assistant)?.Text,
        _model.Files.Count,
        _model.ValidationOk,
        _model.PromptTokens,
        _model.CompletionTokens,
        _model.TokensAreEstimated));

    private void SyncActivity()
    {
        Activity.Clear();
        var fallback = _strings[StudioStringKeys.WizardAgentFallback];
        foreach (var task in _model.Activity)
        {
            // The task id joins the role rather than replacing it: a crew where one agent
            // owns two tasks rendered the same sentence twice, with nothing to tell the
            // reader which of the two had just finished.
            var role = task.AgentRole ?? task.TaskId ?? fallback;
            var text = task.Success
                ? string.Format(
                    CultureInfo.CurrentCulture, _strings[StudioStringKeys.WizardActivityDone],
                    role, Math.Round(task.DurationMs / 1000.0, 1))
                : string.Format(CultureInfo.CurrentCulture, _strings[StudioStringKeys.WizardActivityFailed], role);

            var detail = task.AgentRole is not null && task.TaskId is { Length: > 0 } id ? id : "";
            Activity.Add(new WizardActivityLine(text, detail, task.Success));
        }
    }

    private void SyncChecklist()
    {
        Checklist.Clear();
        foreach (var item in _model.BuildChecklist())
            Checklist.Add(new WizardChecklistLine(item.Statement, item.Passed, item.Detail));
    }

    private void SyncDecisions()
    {
        Decisions.Clear();
        foreach (var option in _model.DecisionOptions)
        {
            // "edit" is not a button: the agent editor's Save is its whole UI — it sends
            // the decision and the amended blueprint itself.
            if (string.Equals(option, "edit", StringComparison.Ordinal))
                continue;

            var label = option switch
            {
                "accept" => _strings[StudioStringKeys.WizardDecisionAccept],
                "retry" => _strings[StudioStringKeys.WizardDecisionRetry],
                "refine" => _strings[StudioStringKeys.WizardDecisionRefine],
                "abort" => _strings[StudioStringKeys.WizardDecisionAbort],
                _ => option,
            };
            Decisions.Add(new WizardDecision(option, label, Decide));
        }
    }

    private List<WizardChoice> Choices(params (string LabelKey, string Key)[] entries)
    {
        var group = new List<WizardChoice>();
        foreach (var (labelKey, key) in entries)
        {
            group.Add(new WizardChoice(key, _strings[labelKey], picked =>
            {
                foreach (var choice in group)
                    choice.IsSelected = ReferenceEquals(choice, picked);
                OnPropertiesChanged(nameof(CanCompose), nameof(OutcomeRequired), nameof(Step1Hint));
                ComposeCommand.RaiseCanExecuteChanged();
            }));
        }

        return group;
    }
}
