using System.Windows;
using System.Windows.Media;
using Orkeon.Studio.Wpf.Controls;
using Orkeon.Studio.Wpf.Services;

namespace Orkeon.Studio.Wpf.Views;

public partial class MainWindow : Window
{
    private bool _dark;

    public MainWindow()
    {
        InitializeComponent();
        UpdateLangButtons();
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
        _dark = !_dark;
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var tokens = new ResourceDictionary
        {
            Source = new Uri($"/Themes/Tokens.{(_dark ? "Dark" : "Light")}.xaml", UriKind.Relative),
        };
        // Tokens.*.xaml is merged FIRST in App.xaml — replace slot 0.
        dictionaries[0] = tokens;
        ThemeIcon.Kind = _dark ? "sun" : "moon";
        ThemeBtn.ToolTip = I18n.T(_dark ? "Theme_ToLight" : "Theme_ToDark");
    }

    // ── language ──
    private void OnLangEn(object sender, RoutedEventArgs e) { I18n.Instance.SetLanguage("en"); UpdateLangButtons(); }
    private void OnLangFr(object sender, RoutedEventArgs e) { I18n.Instance.SetLanguage("fr"); UpdateLangButtons(); }

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
