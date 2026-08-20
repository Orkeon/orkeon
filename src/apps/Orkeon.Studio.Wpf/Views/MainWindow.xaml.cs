using System.Windows;
using System.Windows.Media;
using Orkeon.Studio.Wpf.Controls;
using Orkeon.Studio.Wpf.Services;

namespace Orkeon.Studio.Wpf.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        UpdateLangButtons();
        UpdateThemeButton();
        DataContextChanged += (_, _) => WireForgeNavigation();
    }

    /// <summary>
    /// The two Atelier gestures that move the navigation: an activated session brings the
    /// "Nouveau problème" screen forward, a "Relancer" lands on the launcher (the target
    /// path itself is wired in the ViewModel — this is only the visible panel).
    /// </summary>
    private void WireForgeNavigation()
    {
        if (DataContext is not ViewModels.Shell.MainWindowViewModel shell)
            return;

        shell.Forge.SessionActivated += (_, _) => NavForgeNew.IsChecked = true;
        shell.Forge.RelaunchRequested += (_, _) => NavRun.IsChecked = true;
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
        UiPreferences.Save(ThemeManager.IsDark, I18n.Instance.Language);
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
        UpdateLangButtons();
        UpdateThemeButton();
        UiPreferences.Save(ThemeManager.IsDark, I18n.Instance.Language);
    }

    private void UpdateLangButtons()
    {
        var fr = I18n.Instance.Language == "fr";
        StyleLang(LangEnBtn, !fr);
        StyleLang(LangFrBtn, fr);
    }

    private static void StyleLang(System.Windows.Controls.Button b, bool on)
    {
        if (on)
        {
            b.SetResourceReference(BackgroundProperty, "AccentBrush");
            b.SetResourceReference(ForegroundProperty, "AccentFgBrush");
        }
        else
        {
            b.Background = Brushes.Transparent;
            b.SetResourceReference(ForegroundProperty, "InkMutedBrush");
        }
    }

    // ── guided tour ──
    private void OnStartTour(object sender, RoutedEventArgs e)
    {
        Tour.Start(
        [
            new TourStep(null, "Tour1_Title", "Tour1_Body"),
            new TourStep("Sidebar", "Tour2_Title", "Tour2_Body"),
            new TourStep("SettingsCard", "Tour3_Title", "Tour3_Body"),
            new TourStep("StartActions", "Tour4_Title", "Tour4_Body", () => NavStart.IsChecked = true),
            new TourStep("PresetCard", "Tour5_Title", "Tour5_Body", () => NavStart.IsChecked = true),
            new TourStep("ChainCard", "Tour6_Title", "Tour6_Body", () => NavStart.IsChecked = true),
            new TourStep("MountsPanel", "Tour7_Title", "Tour7_Body", () => NavMounts.IsChecked = true),
            new TourStep("RunPanel", "Tour8_Title", "Tour8_Body", () => NavRun.IsChecked = true),
            new TourStep("DiagPanel", "Tour9_Title", "Tour9_Body", () => NavDiag.IsChecked = true),
        ]);
    }
}
