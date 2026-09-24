using System.ComponentModel;
using System.Globalization;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Wpf.ViewModels.Config;
using Orkeon.Studio.Wpf.ViewModels.Launch;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;
using Orkeon.Studio.Wpf.ViewModels.Teams;

namespace Orkeon.Studio.Wpf.ViewModels.Shell;

/// <summary>The activities that can run at once, each with an engine of its own (STUDIO-34, DD-2).</summary>
public enum StatusBarActivity
{
    /// <summary>The Run screen's launcher.</summary>
    Launch,

    /// <summary>The Test screen's own launcher — a trial runs beside a launch, not instead of it.</summary>
    Test,

    /// <summary>The assistant composing or trying a team: the create-a-team wizard's engine.</summary>
    Atelier,
}

/// <summary>Which activity's screen a click on the status bar asks for (D-04).</summary>
public sealed class StatusBarOpenEventArgs : EventArgs
{
    /// <summary>Names the activity whose group was clicked.</summary>
    public StatusBarOpenEventArgs(StatusBarActivity activity) => Activity = activity;

    /// <summary>The activity whose screen the shell brings forward.</summary>
    public StatusBarActivity Activity { get; }
}

/// <summary>
/// What the status bar watches. Every entry is optional so a test names only the activity its
/// case needs; an activity left out simply never puts a group on the bar.
/// </summary>
public sealed record StatusBarSources
{
    /// <summary>The Run screen's launcher.</summary>
    public LaunchTabViewModel? Launch { get; init; }

    /// <summary>The Test screen's launcher — a second engine, with its own run.</summary>
    public LaunchTabViewModel? Test { get; init; }

    /// <summary>The wizard's progress card, which already reads the assistant's engine.</summary>
    public ComposeProgressViewModel? Atelier { get; init; }

    /// <summary>The model profiles; the default one is what the bar names at rest.</summary>
    public ModelProfilesViewModel? Profiles { get; init; }

    /// <summary>The window's Novice/Expert switch; novice when absent.</summary>
    public UiModeViewModel? Mode { get; init; }

    /// <summary>The localized strings; the English set when absent.</summary>
    public IStudioStrings? Strings { get; init; }

    /// <summary>The beat the clocks on the bar move on; one that never beats when absent.</summary>
    public IUiTicker? Ticker { get; init; }
}

/// <summary>
/// The bar at the foot of the window (STUDIO-34): what runs, what it spends and which tools are at
/// work, without changing screen.
/// <para>
/// One group per activity — Run, Test, the assistant — each on the bar only while its activity
/// runs; none is hidden behind another and none is merged into another (DD-2). At rest, the
/// expert reads the default model profile: there is no «active» profile, each team picks its
/// own, and during a run only the model the meter reports for the agents' calls is true. A
/// segment nothing measured is absent, never a zero. The novice reads each group's state and
/// meters; the expert reads everything (D-03). A click on a group asks the shell for that
/// activity's screen (D-04).
/// </para>
/// <para>
/// Everything here is read off models the screens already hold — the launchers' progress models
/// (STUDIO-30) and the wizard's progress card — so the bar never parses the raw stream, and the
/// type holds no view logic: its whole behaviour is asserted without a window.
/// </para>
/// </summary>
public sealed class StatusBarViewModel : ObservableObject
{
    /// <summary>How often the clocks on the bar move while a run goes.</summary>
    private static readonly TimeSpan Beat = TimeSpan.FromSeconds(1);

    private readonly IStudioStrings _strings;
    private readonly UiModeViewModel _mode;
    private readonly IUiTicker _ticker;
    private readonly ModelProfilesViewModel? _profiles;
    private bool _beating;

    /// <summary>Builds the bar over the activities it watches.</summary>
    public StatusBarViewModel(StatusBarSources? sources = null)
    {
        var wired = sources ?? new StatusBarSources();
        _strings = wired.Strings ?? EnglishStudioStrings.Instance;
        _mode = wired.Mode ?? new UiModeViewModel();
        _ticker = wired.Ticker ?? NullUiTicker.Instance;
        _profiles = wired.Profiles;

        Launch = new StatusBarRunGroupViewModel(StatusBarActivity.Launch, wired.Launch, _strings, IsExpert, Open);
        Test = new StatusBarRunGroupViewModel(StatusBarActivity.Test, wired.Test, _strings, IsExpert, Open);
        Atelier = new StatusBarAtelierGroupViewModel(wired.Atelier, _strings, IsExpert, Open);

        Launch.PropertyChanged += OnGroupChanged;
        Test.PropertyChanged += OnGroupChanged;
        Atelier.PropertyChanged += OnGroupChanged;
        _mode.PropertyChanged += OnModeChanged;
        _strings.CultureChanged += (_, _) => OnCultureChanged();
        if (_profiles is not null)
            _profiles.PropertyChanged += OnProfilesChanged;
    }

    /// <summary>The Run screen's group.</summary>
    public StatusBarRunGroupViewModel Launch { get; }

    /// <summary>The Test screen's group.</summary>
    public StatusBarRunGroupViewModel Test { get; }

    /// <summary>The assistant's group, while it composes or tries a team.</summary>
    public StatusBarAtelierGroupViewModel Atelier { get; }

    /// <summary>The Balance segment, reserved for STUDIO-35: empty until a reading fills it.</summary>
    public StatusBarBalanceViewModel Balance { get; } = new();

    /// <summary>Whether nothing runs — no group on the bar.</summary>
    public bool IsAtRest => !Launch.IsActive && !Test.IsActive && !Atelier.IsActive;

    /// <summary>
    /// «provider · model» of the default profile, what the next launch of a team on the default
    /// runs on; null when no default is elected or it names neither.
    /// </summary>
    public string? Profile => _profiles?.Set.Default is { } profile
        ? StatusBarText.Join([profile.Provider, profile.Model], StatusBarText.Separator)
        : null;

    /// <summary>Names the default profile the provider and the model come from.</summary>
    public string? ProfileTip => _profiles?.Set.Default is { } profile
        ? string.Format(CultureInfo.CurrentCulture, _strings[StudioStringKeys.StatusBarProfileTip], profile.Name)
        : null;

    /// <summary>The profile shows at rest, to the expert, when there is one to show.</summary>
    public bool ShowsProfile => IsAtRest && _mode.IsExpert && Profile is not null;

    /// <summary>Raised by a click on a group: the shell brings its activity's screen forward.</summary>
    public event EventHandler<StatusBarOpenEventArgs>? OpenRequested;

    private bool IsExpert() => _mode.IsExpert;

    private void Open(StatusBarActivity activity) =>
        OpenRequested?.Invoke(this, new StatusBarOpenEventArgs(activity));

    private void OnGroupChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StatusBarRunGroupViewModel.IsActive))
            OnActivityChanged();
    }

    /// <summary>
    /// A group came or went. The clocks beat only while a run goes — at rest nothing ticks — and
    /// the profile shows only at rest. The end of an activity is also where STUDIO-35 reads the
    /// balance again.
    /// </summary>
    private void OnActivityChanged()
    {
        var clocksMove = Launch.IsActive || Test.IsActive;
        if (clocksMove && !_beating)
        {
            _ticker.StartBeat(Beat, Tick);
            _beating = true;
        }
        else if (!clocksMove && _beating)
        {
            _ticker.StopBeat();
            _beating = false;
        }

        OnPropertiesChanged(nameof(IsAtRest), nameof(ShowsProfile));
    }

    private void Tick()
    {
        Launch.RefreshClock();
        Test.RefreshClock();
    }

    private void OnModeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(UiModeViewModel.IsExpert))
            return;

        Launch.RefreshMode();
        Test.RefreshMode();
        Atelier.RefreshMode();
        OnPropertyChanged(nameof(ShowsProfile));
    }

    private void OnCultureChanged()
    {
        Launch.RefreshAll();
        Test.RefreshAll();
        Atelier.RefreshAll();
        OnPropertiesChanged(nameof(Profile), nameof(ProfileTip));
    }

    private void OnProfilesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ModelProfilesViewModel.Set))
            OnPropertiesChanged(nameof(Profile), nameof(ProfileTip), nameof(ShowsProfile));
    }
}
