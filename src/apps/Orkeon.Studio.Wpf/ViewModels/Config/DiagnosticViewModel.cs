using System.Collections.ObjectModel;
using System.Globalization;
using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Config;

/// <summary>One line of the <c>orkeon doctor --json</c> report.</summary>
public sealed class DoctorCheckViewModel
{
    private readonly IStudioStrings _strings;

    /// <summary>Wraps a parsed check.</summary>
    public DoctorCheckViewModel(DoctorCheck check, IStudioStrings? strings = null)
    {
        ArgumentNullException.ThrowIfNull(check);

        Check = check;
        _strings = strings ?? EnglishStudioStrings.Instance;
    }

    /// <summary>
    /// The plain-language name of the check (audit 09/20), resolved per identifier from
    /// the localization port; the raw identifier stays the expert detail. An unknown
    /// check degrades to its own identifier, never to a blank.
    /// </summary>
    public string FriendlyName
    {
        get
        {
            // Keyed by the CLI's own check name, so the key is computed rather than
            // written: «Studio.Diagnostics.Check.» + what doctor called it.
            var key = CheckKeyPrefix + Name;
            var localized = _strings[key];
            return localized == key ? Name : localized;
        }
    }

    /// <summary>Namespace of the per-check overlay, e.g. <c>Studio.Diagnostics.Check.llm-config</c>.</summary>
    public const string CheckKeyPrefix = "Studio.Diagnostics.Check.";

    /// <summary>The underlying Core record.</summary>
    public DoctorCheck Check { get; }

    /// <summary>The name of the check.</summary>
    public string Name => Check.Check;

    /// <summary>Its outcome.</summary>
    public DoctorStatus Status => Check.Status;

    /// <summary>The explanation the CLI printed — English only, so it is Expert detail.</summary>
    public string Detail => Check.Detail;

    /// <summary>
    /// What the check means, in the user's own language. The CLI prints its detail in
    /// English whatever the UI speaks, so a Novice screen quoting it verbatim is a French
    /// or Chinese page with an English sentence in the middle of it. This says the same
    /// thing in one line; the raw text stays, under the mono identifier, for Expert.
    /// </summary>
    public string StatusSentence => _strings[Status switch
    {
        DoctorStatus.Ok => StudioStringKeys.DoctorStatusOk,
        DoctorStatus.Warning => StudioStringKeys.DoctorStatusWarning,
        DoctorStatus.Failure => StudioStringKeys.DoctorStatusFailure,
        _ => StudioStringKeys.DoctorStatusUnknown,
    }];

    /// <summary>A short status glyph, so the view needs no converter.</summary>
    public string Glyph => Status switch
    {
        DoctorStatus.Ok => "✔",        // check mark
        DoctorStatus.Warning => "⚠",   // warning sign
        DoctorStatus.Failure => "✖",   // heavy multiplication X
        _ => "?",
    };

    /// <inheritdoc />
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Glyph} {Name} — {Detail}");
}

/// <summary>
/// One orphan workshop session of the Diagnostic screen (STUDIO-27, D-08): an adopted session
/// whose team folder is gone. « Clean » arms an in-place confirmation; only that deletes.
/// </summary>
public sealed class OrphanSessionViewModel : ObservableObject
{
    private bool _isConfirmingClean;

    internal OrphanSessionViewModel(ForgeSolutionSummary summary, DiagnosticViewModel owner, IStudioStrings strings)
    {
        Summary = summary;
        FolderLine = string.Format(CultureInfo.CurrentCulture, strings[StudioStringKeys.DiagOrphanFolder], summary.PromotedTo);
        AskCleanCommand = new RelayCommand(() => owner.ArmClean(this));
        ConfirmCleanCommand = new RelayCommand(() => owner.Clean(this));
        CancelCleanCommand = new RelayCommand(() => IsConfirmingClean = false);
    }

    /// <summary>The session, as the forge catalog listed it.</summary>
    public ForgeSolutionSummary Summary { get; }

    /// <summary>Its title, falling back to its folder name.</summary>
    public string Title => Summary.Title is { Length: > 0 } title ? title : Summary.Slug;

    /// <summary>« Its team folder is gone: … » — the folder it names.</summary>
    public string FolderLine { get; }

    /// <summary>« Clean »: arms the confirmation, deletes nothing on its own.</summary>
    public RelayCommand AskCleanCommand { get; }

    /// <summary>Deletes the session directory — only reachable from the armed confirmation.</summary>
    public RelayCommand ConfirmCleanCommand { get; }

    /// <summary>Disarms the confirmation.</summary>
    public RelayCommand CancelCleanCommand { get; }

    /// <summary>Whether the row shows its confirmation in place of « Clean ».</summary>
    public bool IsConfirmingClean
    {
        get => _isConfirmingClean;
        internal set
        {
            if (SetProperty(ref _isConfirmingClean, value))
                OnPropertyChanged(nameof(IsIdle));
        }
    }

    /// <summary>The row's own « Clean » visibility — the confirmation takes its place.</summary>
    public bool IsIdle => !_isConfirmingClean;
}

/// <summary>
/// The "Diagnostic" button of spec §4.4: runs <c>orkeon doctor --json</c> as a child process and
/// renders the parsed report. The CLI is the authority — Studio does not re-implement any check.
/// Beside it, the orphan workshop sessions (STUDIO-27, D-08): adopted sessions whose team folder
/// is gone, listed so that they can be cleaned — never without the user.
/// </summary>
public sealed class DiagnosticViewModel : ObservableObject
{
    private readonly OrkeonProcessRunner _runner;
    private readonly IUiDispatcher _dispatcher;
    private readonly IStudioStrings _strings;
    private readonly string? _forgeWorkspace;
    private readonly string? _teamsRoot;
    private DoctorReport? _lastReport;
    private string? _summary;
    private string? _errorMessage;
    private bool _hasRun;
    private string _orphanMessage = "";

    /// <summary>Binds the panel to the process runner that will invoke the CLI.</summary>
    /// <param name="runner">The runner every screen invokes the CLI through.</param>
    /// <param name="dispatcher">Where a continuation lands; the immediate one when null.</param>
    /// <param name="strings">The localized strings; English when null.</param>
    /// <param name="forgeWorkspace">
    /// The workspace the workshop sessions live under (STUDIO-27, D-08) — what the orphan list
    /// reads. Null lists none.
    /// </param>
    /// <param name="teamsRoot">
    /// The teams directory: a team moved or renamed there is found by its id, so its session is no
    /// orphan. Null consults none.
    /// </param>
    public DiagnosticViewModel(
        OrkeonProcessRunner runner,
        IUiDispatcher? dispatcher = null,
        IStudioStrings? strings = null,
        string? forgeWorkspace = null,
        string? teamsRoot = null)
    {
        ArgumentNullException.ThrowIfNull(runner);

        _runner = runner;
        _dispatcher = dispatcher ?? ImmediateUiDispatcher.Instance;
        _strings = strings ?? EnglishStudioStrings.Instance;
        _forgeWorkspace = forgeWorkspace;
        _teamsRoot = teamsRoot;

        // Only the verdict line re-describes on a language switch: the check names and details
        // are `orkeon doctor`'s own output and stay as the CLI printed them (STUDIO-11 decision).
        _strings.CultureChanged += (_, _) =>
        {
            if (_lastReport is { } report)
            {
                Summary = Describe(report);
                OnPropertyChanged(nameof(VerdictHeadline));
                OnPropertyChanged(nameof(VerdictDetail));
            }
        };

        RunCommand = new AsyncRelayCommand(() => RunAsync());
    }

    /// <summary>
    /// The silent first run (audit 09/20, T-09): the window triggers it once at startup so
    /// the sidebar dot and the verdict card are honest before the user ever presses the
    /// button. A missing CLI comes back as a NotStarted report, not an exception.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await RunAsync(null, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // The window is closing; nothing to surface.
        }
    }

    /// <summary>Runs the diagnostic.</summary>
    public AsyncRelayCommand RunCommand { get; }

    /// <summary>The parsed checks of the last run.</summary>
    public ObservableCollection<DoctorCheckViewModel> Checks { get; } = [];

    /// <summary>A one-line verdict over <see cref="Checks"/>.</summary>
    public string? Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    /// <summary>Why the report is unusable: the CLI is missing, or its output could not be parsed.</summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    /// <summary>Whether a diagnostic has completed at least once.</summary>
    public bool HasRun
    {
        get => _hasRun;
        private set => SetProperty(ref _hasRun, value);
    }

    /// <summary>Whether the diagnostic is still in flight.</summary>
    public bool IsRunning => RunCommand.IsRunning;

    /// <summary>How many checks came back green.</summary>
    public int OkCount => Checks.Count(c => c.Status == DoctorStatus.Ok);

    /// <summary>How many checks warned.</summary>
    public int WarningCount => Checks.Count(c => c.Status == DoctorStatus.Warning);

    /// <summary>How many checks failed (a parse error counts as one — the report is unusable).</summary>
    public int FailureCount =>
        Checks.Count(c => c.Status == DoctorStatus.Failure) + (ErrorMessage is { Length: > 0 } ? 1 : 0);

    /// <summary>"Everything is in place." / "One point to fix…" — the verdict card's headline.</summary>
    public string VerdictHeadline => _strings[
        HasIssues ? StudioStringKeys.DiagFixNeeded : StudioStringKeys.DiagAllGood];

    /// <summary>"{0} checks passed, {1} warning(s), {2} failure(s)." under the headline.</summary>
    public string VerdictDetail => string.Format(
        CultureInfo.CurrentCulture, _strings[StudioStringKeys.DiagCounts],
        OkCount, WarningCount, FailureCount);

    /// <summary>There is something to copy once a run has produced checks or an error.</summary>
    /// <summary>The "copied!" feedback of the header button; the view resets it after ~1.6 s.</summary>
    public bool ReportCopied
    {
        get => _reportCopied;
        set => SetProperty(ref _reportCopied, value);
    }

    private bool _reportCopied;

    public bool CanCopyReport => HasRun && (Checks.Count > 0 || ErrorMessage is { Length: > 0 });

    /// <summary>
    /// The last report as plain text, for the clipboard: the verdict line, one line per
    /// check (glyph, name, detail — the CLI's own untranslated output, STUDIO-11), and the
    /// parse error when there is one. WPF text blocks are not selectable; this is how the
    /// operator gets the result out of the window.
    /// </summary>
    public string BuildReport()
    {
        var lines = new List<string> { "orkeon doctor" };
        if (Summary is { Length: > 0 } summary)
            lines.Add(summary);

        if (Checks.Count > 0)
        {
            lines.Add("");
            lines.AddRange(Checks.Select(check => check.ToString()));
        }

        if (ErrorMessage is { Length: > 0 } error)
        {
            lines.Add("");
            lines.Add(error);
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// True when the last run surfaced anything other than green — the sidebar shows a warn
    /// dot on the Diagnostic entry so the operator learns before a team fails mid-run.
    /// </summary>
    public bool HasIssues =>
        HasRun && (ErrorMessage is { Length: > 0 } || Checks.Any(c => c.Status != DoctorStatus.Ok));

    /// <summary>The orphan workshop sessions of the last run (STUDIO-27, D-08).</summary>
    public ObservableCollection<OrphanSessionViewModel> OrphanSessions { get; } = [];

    /// <summary>Whether the orphan section shows.</summary>
    public bool HasOrphanSessions => OrphanSessions.Count > 0;

    /// <summary>What the last clean could not do; empty while nothing failed.</summary>
    public string OrphanMessage
    {
        get => _orphanMessage;
        private set
        {
            if (SetProperty(ref _orphanMessage, value))
                OnPropertyChanged(nameof(HasOrphanMessage));
        }
    }

    /// <summary>Whether the orphan section says what a clean could not do.</summary>
    public bool HasOrphanMessage => _orphanMessage.Length > 0;

    /// <summary>Arms one row's confirmation and disarms every other: one question at a time.</summary>
    internal void ArmClean(OrphanSessionViewModel row)
    {
        foreach (var orphan in OrphanSessions)
            orphan.IsConfirmingClean = ReferenceEquals(orphan, row);
    }

    /// <summary>Deletes the session the user confirmed; a directory the disk keeps stays listed, said so.</summary>
    internal void Clean(OrphanSessionViewModel row)
    {
        if (!ForgeSessionCatalog.Delete(row.Summary.Directory))
        {
            row.IsConfirmingClean = false;
            OrphanMessage = _strings[StudioStringKeys.DiagOrphanCleanFailed];
            return;
        }

        OrphanMessage = "";
        OrphanSessions.Remove(row);
        OnPropertyChanged(nameof(HasOrphanSessions));
    }

    /// <summary>The orphans as the disk holds them now; none without a workspace.</summary>
    private IReadOnlyList<ForgeSolutionSummary> FindOrphans() =>
        _forgeWorkspace is { Length: > 0 } workspace ? ForgeSessionCatalog.FindOrphans(workspace, _teamsRoot) : [];

    /// <summary>Runs <c>orkeon doctor --json</c> and republishes the panel.</summary>
    public async Task<DoctorReport> RunAsync(
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        OnPropertyChanged(nameof(IsRunning));

        var report = await _runner.RunDoctorAsync(workingDirectory, onOutput: null, cancellationToken);
        var orphans = FindOrphans();

        _dispatcher.Post(() =>
        {
            OrphanSessions.Clear();
            foreach (var orphan in orphans)
                OrphanSessions.Add(new OrphanSessionViewModel(orphan, this, _strings));
            OrphanMessage = "";
            OnPropertyChanged(nameof(HasOrphanSessions));

            Checks.Clear();
            foreach (var check in report.Checks)
                Checks.Add(new DoctorCheckViewModel(check, _strings));

            _lastReport = report;
            ErrorMessage = report.ParseError;
            Summary = Describe(report);
            HasRun = true;
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(HasIssues));
            OnPropertyChanged(nameof(CanCopyReport));
            OnPropertyChanged(nameof(OkCount));
            OnPropertyChanged(nameof(WarningCount));
            OnPropertyChanged(nameof(FailureCount));
            OnPropertyChanged(nameof(VerdictHeadline));
            OnPropertyChanged(nameof(VerdictDetail));
        });

        return report;
    }

    private string Describe(DoctorReport report)
    {
        if (report.Run.Outcome == RunOutcome.NotStarted)
            return report.Run.Description;

        if (report.Checks.Count == 0)
            return report.ParseError ?? _strings[StudioStringKeys.DiagNoCheck];

        var failures = report.Checks.Count(c => c.Status == DoctorStatus.Failure);
        var warnings = report.Checks.Count(c => c.Status == DoctorStatus.Warning);

        return failures == 0 && warnings == 0
            ? string.Format(
                CultureInfo.InvariantCulture,
                _strings[StudioStringKeys.DiagAllGreen], report.Checks.Count)
            : string.Format(
                CultureInfo.InvariantCulture,
                _strings[StudioStringKeys.DiagFindings], report.Checks.Count, failures, warnings);
    }
}
