using System.Collections.ObjectModel;
using System.Globalization;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Config;

/// <summary>One line of the <c>orkeon doctor --json</c> report.</summary>
public sealed class DoctorCheckViewModel
{
    /// <summary>Wraps a parsed check.</summary>
    public DoctorCheckViewModel(DoctorCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);

        Check = check;
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
                Summary = Describe(report);
        };

        RunCommand = new AsyncRelayCommand(() => RunAsync());
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
                Checks.Add(new DoctorCheckViewModel(check));

            _lastReport = report;
            ErrorMessage = report.ParseError;
            Summary = Describe(report);
            HasRun = true;
            OnPropertyChanged(nameof(IsRunning));
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
