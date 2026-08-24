using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Orkeon.Studio.Wpf.Controls;
using Orkeon.Studio.Wpf.Services;

namespace Orkeon.Studio.Wpf.Views;

public partial class MainWindow : Window
{
    private bool _splashDismissed;

    public MainWindow()
    {
        InitializeComponent();
        UpdateThemeButton();
        DataContextChanged += (_, _) => WireForgeNavigation();
        BeginSplash();
    }

    /// <summary>
    /// The gestures that move the navigation: an activated wizard session brings "Créer une
    /// équipe" forward, "Lancer" on a team lands on the launcher, the gate's "Gérer les
    /// réglages" opens the settings screen (the targets themselves are wired in the
    /// ViewModels — this is only the visible panel).
    /// </summary>
    private void WireForgeNavigation()
    {
        if (DataContext is not ViewModels.Shell.MainWindowViewModel shell)
            return;

        shell.CreateTeam.SessionActivated += (_, _) => NavCreate.IsChecked = true;
        shell.CreateTeam.OpenSettingsRequested += (_, _) => NavSettings.IsChecked = true;
        shell.Teams.CreateRequested += (_, _) => NavCreate.IsChecked = true;
        shell.TestRequested += (_, _) => NavTest.IsChecked = true;
        shell.Teams.LaunchRequested += (_, _) => NavRun.IsChecked = true;
        shell.Teams.ResumeRequested += (_, _) => NavCreate.IsChecked = true;
        shell.Import.TeamImported += (_, _) => NavTeams.IsChecked = true;
        // The lists refresh on arrival: a session stopped mid-wizard, or a folder dropped in
        // by hand, shows up without waiting for an adopt or an app restart.
        NavTeams.Checked += (_, _) => { shell.Teams.Refresh(); shell.Test.RefreshTeams(); };
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
        UiPreferences.Save(ThemeManager.IsDark, I18n.Instance.Language, CurrentMode());
    }

    private void UpdateThemeButton()
    {
        ThemeIcon.Kind = ThemeManager.IsDark ? "sun" : "moon";
        ThemeBtn.ToolTip = I18n.T(ThemeManager.IsDark ? "Theme_ToLight" : "Theme_ToDark");
    }

    // ── language ──
    private void OnLangEn(object sender, RoutedEventArgs e) => SetLanguage("en");
    private void OnLangFr(object sender, RoutedEventArgs e) => SetLanguage("fr");

    private void SetLanguage(string language)
    {
        I18n.Instance.SetLanguage(language);
        UpdateThemeButton();
        UiPreferences.Save(ThemeManager.IsDark, I18n.Instance.Language, CurrentMode());
    }

    private string CurrentMode() =>
        (DataContext as ViewModels.Shell.MainWindowViewModel)?.Mode.Mode
        ?? ViewModels.Shell.UiModeViewModel.Novice;



    // ── guided tour (v3: five stops — the mode, the three sidebar groups, the help column) ──
    private void OnStartTour(object sender, RoutedEventArgs e)
    {
        Tour.Start(
        [
            new TourStep("ModeSwitch", "Tour1_Title", "Tour1_Body"),
            new TourStep("NavGroupTeams", "Tour2_Title", "Tour2_Body"),
            new TourStep("NavGroupWork", "Tour3_Title", "Tour3_Body"),
            new TourStep("NavGroupEnv", "Tour4_Title", "Tour4_Body"),
            new TourStep(null, "Tour5_Title", "Tour5_Body"),
        ]);
    }

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
    private void BeginSplash()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4.55) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            DismissSplash(TimeSpan.FromMilliseconds(450));
        };
        timer.Start();
    }

    private void OnSplashClick(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        DismissSplash(TimeSpan.FromMilliseconds(150));

    /// <summary>The screenshot campaign needs the plate gone instantly, not animated away.</summary>
    internal void SkipSplashForCapture() => DismissSplash(TimeSpan.Zero);

    private void DismissSplash(TimeSpan fade)
    {
        if (_splashDismissed)
            return;

        _splashDismissed = true;
        var animation = new DoubleAnimation(0, new Duration(fade));
        animation.Completed += (_, _) => Splash.Visibility = Visibility.Collapsed;
        Splash.BeginAnimation(OpacityProperty, animation);
    }
}
