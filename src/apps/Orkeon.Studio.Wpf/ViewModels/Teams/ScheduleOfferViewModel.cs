using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Teams;

/// <summary>Payload of a schedule that changed: the team folder, and where its schedule now stands.</summary>
public sealed class TeamScheduleChangedEventArgs(string path, TeamScheduleState state) : EventArgs
{
    /// <summary>Absolute path of the team folder.</summary>
    [SuppressMessage("Minor Code Smell", "S3604:Member initializer values should not be redundant",
        Justification = "False positive on a primary constructor: the initializer IS the only "
                      + "assignment of the member, and removing it would leave it unset.")]
    public string Path { get; } = path;

    /// <summary>The state the engine reported after the gesture.</summary>
    [SuppressMessage("Minor Code Smell", "S3604:Member initializer values should not be redundant",
        Justification = "False positive on a primary constructor: the initializer IS the only "
                      + "assignment of the member, and removing it would leave it unset.")]
    public TeamScheduleState State { get; } = state;
}

/// <summary>
/// The question an adoption asks when the team is scheduled (STUDIO-27, D-05): « Install the
/// schedule (every day at 08:00)? ». « Install » runs <c>forge schedule</c> — the engine does the
/// operating system's part — and « Later » leaves the team installable from its card. A
/// re-adoption asks only when the schedule is not installed as declared any more: one that still
/// stands is said to stand. The outcome stays on screen as one line, with the command a person
/// can run by hand when the system refused.
/// </summary>
public sealed class ScheduleOfferViewModel : ObservableObject
{
    private readonly ForgeClient _client;
    private readonly IUiDispatcher _dispatcher;
    private readonly IStudioStrings _strings;
    private string? _teamPath;
    private string? _expression;
    private bool _isOpen;
    private string _outcome = "";
    private string? _manualCommand;

    /// <summary>Builds the offer over the engine client the wizard drives.</summary>
    public ScheduleOfferViewModel(ForgeClient client, IUiDispatcher dispatcher, IStudioStrings strings)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _strings = strings ?? throw new ArgumentNullException(nameof(strings));
        InstallCommand = new AsyncRelayCommand(InstallAsync, () => IsOpen);
        LaterCommand = new RelayCommand(Decline, () => IsOpen);
        _strings.CultureChanged += (_, _) => OnPropertyChanged(nameof(Question));
    }

    /// <summary>Raised once the engine said where the team's schedule stands after « Install ».</summary>
    public event EventHandler<TeamScheduleChangedEventArgs>? ScheduleChanged;

    /// <summary>Whether the question is on screen, waiting for an answer.</summary>
    public bool IsOpen
    {
        get => _isOpen;
        private set
        {
            if (!SetProperty(ref _isOpen, value))
                return;

            InstallCommand.RaiseCanExecuteChanged();
            LaterCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>« Install the schedule (every day at 08:00)? » — in the grammar's two shapes.</summary>
    public string Question => _expression switch
    {
        null => "",
        var daily when daily.StartsWith("daily@", StringComparison.OrdinalIgnoreCase) =>
            string.Format(CultureInfo.CurrentCulture, _strings[StudioStringKeys.WizardScheduleOfferDaily], daily["daily@".Length..]),
        _ => _strings[StudioStringKeys.WizardScheduleOfferHourly],
    };

    /// <summary>What happened to the offer: installed, left for later, refused, kept; empty while nothing did.</summary>
    public string Outcome
    {
        get => _outcome;
        private set
        {
            if (SetProperty(ref _outcome, value))
                OnPropertyChanged(nameof(HasOutcome));
        }
    }

    /// <summary>Whether the outcome line shows.</summary>
    public bool HasOutcome => _outcome.Length > 0;

    /// <summary>What a person can run by hand, when the system refused Orkeon; null otherwise.</summary>
    public string? ManualCommand
    {
        get => _manualCommand;
        private set
        {
            if (SetProperty(ref _manualCommand, value))
                OnPropertyChanged(nameof(HasManualCommand));
        }
    }

    /// <summary>Whether the manual command shows.</summary>
    public bool HasManualCommand => _manualCommand is { Length: > 0 };

    /// <summary>« Install »: <c>forge schedule</c> on the adopted team.</summary>
    public AsyncRelayCommand InstallCommand { get; }

    /// <summary>« Later »: nothing is installed; the team's card keeps offering it.</summary>
    public RelayCommand LaterCommand { get; }

    /// <summary>
    /// Offers to install <paramref name="expression"/> for the team adopted at <paramref name="teamPath"/>.
    /// With <paramref name="checkFirst"/> — a re-adoption — the engine is asked where the schedule
    /// stands first: installed as declared, nothing is asked and the line says it stays.
    /// </summary>
    internal async Task OfferAsync(string teamPath, string expression, bool checkFirst)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);

        if (checkFirst && await StillInstalledAsync(teamPath).ConfigureAwait(false))
        {
            _dispatcher.Post(() =>
            {
                Close();
                Outcome = _strings[StudioStringKeys.WizardScheduleKept];
            });
            return;
        }

        _dispatcher.Post(() =>
        {
            Close();
            _teamPath = teamPath;
            _expression = expression;
            OnPropertyChanged(nameof(Question));
            IsOpen = true;
        });
    }

    /// <summary>Takes the offer off screen, question and outcome alike — a new creation starts.</summary>
    internal void Close()
    {
        IsOpen = false;
        Outcome = "";
        ManualCommand = null;
    }

    /// <summary>
    /// Whether the engine says the schedule is installed as declared. Whatever it could not say —
    /// a missing binary, a refusal, a child that would not start — reads as « no », and the
    /// question is asked: asking once too often costs a click, never a schedule.
    /// </summary>
    [SuppressMessage("Design", "CA1031",
        Justification = "A check that cannot be made must not cost the offer: any failure reads as not installed.")]
    private async Task<bool> StillInstalledAsync(string teamPath)
    {
        try
        {
            var check = await _client.ScheduleAsync(teamPath, ForgeScheduleVerb.Check).ConfigureAwait(false);
            return check.Succeeded && check.State == TeamScheduleState.Installed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }

    [SuppressMessage("Design", "CA1031",
        Justification = "The launch fault is the refusal the line says: it lands on screen, never in a discarded task.")]
    private async Task InstallAsync()
    {
        if (_teamPath is not { } teamPath)
            return;

        ForgeScheduleReport report;
        try
        {
            report = await _client.ScheduleAsync(teamPath, ForgeScheduleVerb.Install).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _dispatcher.Post(() =>
            {
                IsOpen = false;
                Outcome = string.Format(CultureInfo.CurrentCulture, _strings[StudioStringKeys.TeamsScheduleInstallFailed], ex.Message);
            });
            return;
        }

        _dispatcher.Post(() =>
        {
            IsOpen = false;
            if (report.Succeeded)
            {
                Outcome = _strings[StudioStringKeys.WizardScheduleInstalled];
                ManualCommand = null;
            }
            else
            {
                Outcome = string.Format(
                    CultureInfo.CurrentCulture, _strings[StudioStringKeys.TeamsScheduleInstallFailed], report.FailureReason);
                ManualCommand = report.ManualCommand;
            }

            ScheduleChanged?.Invoke(this, new TeamScheduleChangedEventArgs(
                teamPath, report.Succeeded ? report.State : TeamScheduleState.Unknown));
        });
    }

    private void Decline()
    {
        IsOpen = false;
        Outcome = _strings[StudioStringKeys.WizardScheduleDeclined];
    }
}
