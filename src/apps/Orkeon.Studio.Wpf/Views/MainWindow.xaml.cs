using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Orkeon.Studio.Wpf.Controls;
using Orkeon.Studio.Wpf.Services;

using Orkeon.Studio.Wpf.ViewModels.Teams;

namespace Orkeon.Studio.Wpf.Views;

public partial class MainWindow : Window
{
    /// <summary>The splash progress bar at full travel, as the markup declares it.</summary>
    private const double SplashProgressWidth = 220;

    private bool _splashDismissed;

    public MainWindow()
    {
        InitializeComponent();
        UpdateThemeButton();
        DataContextChanged += (_, _) => WireForgeNavigation();
        BeginSplash();
    }

    /// <summary>
    /// The gestures that move the navigation: an activated wizard session brings the
    /// create-a-team panel forward, running a team lands on the launcher, the gate's
    /// settings link opens the settings screen (the targets themselves are wired in the
    /// ViewModels — this is only the visible panel).
    /// </summary>
    private void WireForgeNavigation()
    {
        if (DataContext is not ViewModels.Shell.MainWindowViewModel shell)
            return;

        shell.CreateTeam.SessionActivated += (_, _) => NavCreate.IsChecked = true;
        shell.CreateTeam.OpenSettingsRequested += (_, _) => NavSettings.IsChecked = true;
        shell.AllowedFolders.OpenSettingsRequested += (_, _) => NavSettings.IsChecked = true;
        shell.Launch.OpenAllowedFoldersRequested += (_, _) => NavSettings.IsChecked = true;
        shell.Launch.ChooseTeamRequested += (_, _) => NavTeams.IsChecked = true;
        shell.Launch.CreateTeamRequested += (_, _) => NavCreate.IsChecked = true;
        shell.Test.Launcher.OpenAllowedFoldersRequested += (_, _) => NavSettings.IsChecked = true;
        shell.Teams.CreateRequested += (_, _) => NavCreate.IsChecked = true;
        shell.Teams.ImportRequested += (_, _) => NavImport.IsChecked = true;
        shell.TestRequested += (_, _) => NavTest.IsChecked = true;
        shell.Teams.LaunchRequested += (_, _) => NavRun.IsChecked = true;
        shell.Teams.ResumeRequested += (_, _) => NavCreate.IsChecked = true;
        shell.Import.TeamImported += (_, _) => NavTeams.IsChecked = true;
        // The lists refresh on arrival: a session stopped mid-wizard, or a folder dropped in
        // by hand, shows up without waiting for an adopt or an app restart.
        NavTeams.Checked += (_, _) => { shell.Teams.Refresh(); shell.Test.RefreshTeams(); };

        // Échap closes the language menu; StaysOpen=False already answers the click
        // elsewhere. Handled on the window because the popup is not in its visual tree.
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape && shell.Language.IsMenuOpen)
            {
                shell.Language.CloseMenuCommand.Execute(null);
                e.Handled = true;
            }
        };

        // The conversation follows the screen it is mounted on: what a free question with
        // no keyword gets back, and what the primer says on an empty thread, both depend
        // on where the user actually stands (T-06).
        NavRun.Checked += (_, _) => shell.Chat.SetContext(AssistantContext.Run);
        NavHistory.Checked += (_, _) => shell.Chat.SetContext(AssistantContext.History);
        NavCreate.Checked += (_, _) => shell.Chat.SetContext(WizardContext(shell));
        shell.CreateTeam.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ViewModels.Teams.CreateTeamViewModel.Step) && NavCreate.IsChecked == true)
                shell.Chat.SetContext(WizardContext(shell));
        };

        shell.Mode.PropertyChanged += (_, e) =>
        {
            // The Tester entry is expert-only: a switch back to novice while it shows would
            // leave a blank content area behind the hidden radio.
            if (e.PropertyName == nameof(ViewModels.Shell.UiModeViewModel.IsNovice)
                && shell.Mode.IsNovice && NavTest.IsChecked == true)
            {
                NavRun.IsChecked = true;
            }
        };
    }

    private static AssistantContext WizardContext(ViewModels.Shell.MainWindowViewModel shell) =>
        shell.CreateTeam.Step switch
        {
            2 => AssistantContext.WizardStep2,
            3 => AssistantContext.WizardStep3,
            4 => AssistantContext.WizardStep4,
            _ => AssistantContext.WizardStep1,
        };

    // ── window chrome ──
    private void OnMinimize(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void OnMaximizeRestore(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }
    private void OnClose(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

    // ── theme ──
    private void OnToggleTheme(object sender, RoutedEventArgs e)
    {
        ThemeManager.Apply(!ThemeManager.IsDark);
        UpdateThemeButton();
        SavePreferences();
    }

    private void UpdateThemeButton()
    {
        ThemeIcon.Kind = ThemeManager.IsDark ? "sun" : "moon";
        ThemeBtn.ToolTip = I18n.T(ThemeManager.IsDark ? "Studio.Shell.ToLight" : "Studio.Shell.ToDark");
    }

    /// <summary>
    /// Writes theme and mode, and the language ONLY if the user picked one. Passing the
    /// running language unconditionally is what froze a detected language on the first
    /// theme toggle — after which a change of Windows language was never followed again.
    /// </summary>
    private void SavePreferences()
    {
        var shell = DataContext as ViewModels.Shell.MainWindowViewModel;
        var chosen = shell?.Language.IsExplicitChoice == true ? shell.Language.Current : null;
        UiPreferences.Save(ThemeManager.IsDark, chosen, CurrentMode());
        UpdateThemeButton();
    }

    private string CurrentMode() =>
        (DataContext as ViewModels.Shell.MainWindowViewModel)?.Mode.Mode
        ?? ViewModels.Shell.UiModeViewModel.Novice;



    // ── guided tour (v3: five stops — the mode, the three sidebar groups, the help column) ──
    private void OnStartTour(object sender, RoutedEventArgs e) => Tour.Start(TourSteps);

    /// <summary>
    /// The overlay's own step type, projected from the catalogue the campaign reads too. The
    /// literals used to live here; two consumers of one list is what keeps a new stop from
    /// existing on screen and nowhere in the collection.
    /// </summary>
    internal static IReadOnlyList<TourStep> TourSteps { get; } =
        [.. ViewModels.Shell.GuidedTourCatalog.Steps.Select(
            step => new TourStep(step.TargetName, step.TitleKey, step.BodyKey))];

    private void OnAboutBackdropClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is ViewModels.Shell.MainWindowViewModel shell)
            shell.About.CloseCommand.Execute(null);
    }

    private void OnProfileEditorBackdropClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is ViewModels.Shell.MainWindowViewModel shell)
            shell.Settings.Profiles.Editor?.CancelCommand.Execute(null);
    }

    private void OnTeamMountsBackdropClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is ViewModels.Shell.MainWindowViewModel shell)
            shell.TeamMounts.CancelCommand.Execute(null);
    }

    private void OnAgentEditorBackdropClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is ViewModels.Shell.MainWindowViewModel shell)
            shell.CreateTeam.AgentEditor.CancelCommand.Execute(null);
    }

    private void OnAllowedFoldersBackdropClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is ViewModels.Shell.MainWindowViewModel shell)
            shell.AllowedFolders.CancelCommand.Execute(null);
    }

    private void OnFolderPickerBackdropClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is ViewModels.Shell.MainWindowViewModel shell)
            shell.FolderPicker.CancelCommand.Execute(null);
    }

    private void OnSwallowClick(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        // The card must not let the click bubble to the backdrop, whose click means "close".
        e.Handled = true;

    private void OnStartTourFromAbout(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.Shell.MainWindowViewModel shell)
            shell.About.CloseCommand.Execute(null);

        OnStartTour(sender, e);
    }

    // ── startup screen ──
    // Five seconds, clickable through: the design's startup plate. The dismissal is animation
    // only — nothing waits on it, and a smoke run closes the window regardless.
    private DispatcherTimer? _splashTimer;

    private void BeginSplash()
    {
        // Kept in a field so a capture campaign can stop it: a 4,55 s tick landing between two
        // shots is a state change nobody asked for, and a timer holding a closed window alive is
        // a loose end even when the guard makes it a no-op.
        _splashTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4.55) };
        _splashTimer.Tick += (_, _) =>
        {
            _splashTimer.Stop();
            DismissSplash(TimeSpan.FromMilliseconds(450));
        };
        _splashTimer.Start();
    }

    private void OnSplashClick(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        DismissSplash(TimeSpan.FromMilliseconds(150));

    /// <summary>
    /// The startup plate, posed for a shot: a plate a user could actually have seen, rather than
    /// the few pixels the progress bar happens to have drawn by the time the campaign gets there.
    /// </summary>
    internal void PoseSplashForCapture(double progress = 0.62)
    {
        _splashTimer?.Stop();
        SplashProgress.BeginAnimation(WidthProperty, null);
        SplashProgress.Width = SplashProgressWidth * progress;
        Splash.BeginAnimation(OpacityProperty, null);
        Splash.Opacity = 1;
        Splash.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// The plate gone, with no animation involved at all.
    /// <para>
    /// This replaces a version that ran a zero-length <see cref="DoubleAnimation"/> and set the
    /// visibility from its <c>Completed</c> handler. A zero DURATION is not a synchronous
    /// completion: <c>BeginAnimation</c> only attaches the clock, and the media context ticks it on
    /// the next render pass — so on return the plate was still fully opaque, and every shot taken
    /// before that tick was a picture of the splash screen. It worked only because the settle slept
    /// afterwards, which is exactly what the campaign no longer does.
    /// </para>
    /// </summary>
    internal void HideSplashForCapture()
    {
        _splashDismissed = true;
        _splashTimer?.Stop();
        Splash.BeginAnimation(OpacityProperty, null);
        Splash.Opacity = 0;
        Splash.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// The theme for a capture: the tokens AND the button that names them.
    /// <para>
    /// <see cref="ThemeManager.Apply"/> on its own leaves the title bar showing a moon in dark
    /// mode, because the icon and its tooltip are refreshed only from the toggle handler — a title
    /// bar contradicting its own window, in the first place a reviewer looks. Deliberately does NOT
    /// call <c>SavePreferences</c>: a campaign must leave the operator's stored theme as it found it.
    /// </para>
    /// </summary>
    internal void ApplyThemeForCapture(bool dark)
    {
        ThemeManager.Apply(dark);
        UpdateThemeButton();
    }

    private void DismissSplash(TimeSpan fade)
    {
        if (_splashDismissed)
            return;

        _splashDismissed = true;
        var animation = new DoubleAnimation(0, new Duration(fade));
        animation.Completed += (_, _) => Splash.Visibility = Visibility.Collapsed;
        Splash.BeginAnimation(OpacityProperty, animation);
    }

    /// <summary>The draft note under the nav: one click puts the unfinished creation back in front.</summary>
    private void OnResumeDraft(object sender, RoutedEventArgs e) => NavCreate.IsChecked = true;
}
