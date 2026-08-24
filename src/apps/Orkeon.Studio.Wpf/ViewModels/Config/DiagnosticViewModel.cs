using System.Collections.ObjectModel;
using System.Globalization;
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
            var key = "Vm_Doctor_" + Name;
            var localized = _strings[key];
            return localized == key ? Name : localized;
        }
    }

    /// <summary>The underlying Core record.</summary>
    public DoctorCheck Check { get; }

    /// <summary>The name of the check.</summary>
    public string Name => Check.Check;

    /// <summary>Its outcome.</summary>
    public DoctorStatus Status => Check.Status;

    /// <summary>The explanation the CLI printed.</summary>
    public string Detail => Check.Detail;

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
/// The "Diagnostic" button of spec §4.4: runs <c>orkeon doctor --json</c> as a child process and
/// renders the parsed report. The CLI is the authority — Studio does not re-implement any check.
/// </summary>
public sealed class DiagnosticViewModel : ObservableObject
{
    private readonly OrkeonProcessRunner _runner;
    private readonly IUiDispatcher _dispatcher;
    private readonly IStudioStrings _strings;
    private DoctorReport? _lastReport;
    private string? _summary;
    private string? _errorMessage;
    private bool _hasRun;

    /// <summary>Binds the panel to the process runner that will invoke the CLI.</summary>
    public DiagnosticViewModel(
        OrkeonProcessRunner runner,
        IUiDispatcher? dispatcher = null,
        IStudioStrings? strings = null)
    {
        ArgumentNullException.ThrowIfNull(runner);

        _runner = runner;
        _dispatcher = dispatcher ?? ImmediateUiDispatcher.Instance;
        _strings = strings ?? EnglishStudioStrings.Instance;

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
    /// <summary>« Copié ! » feedback of the header button; the view resets it after ~1,6 s.</summary>
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

    /// <summary>Runs <c>orkeon doctor --json</c> and republishes the panel.</summary>
    public async Task<DoctorReport> RunAsync(
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        OnPropertyChanged(nameof(IsRunning));

        var report = await _runner.RunDoctorAsync(workingDirectory, onOutput: null, cancellationToken);

        _dispatcher.Post(() =>
        {
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
