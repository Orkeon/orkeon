using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Wpf.ViewModels.Launch;

namespace Orkeon.Studio.Wpf.Views;

/// <summary>The Run screen. Code-behind is XAML wiring only.</summary>
public partial class LaunchTabView : UserControl
{
    /// <summary>Loads the XAML.</summary>
    public LaunchTabView() => InitializeComponent();

    /// <summary>
    /// The settings-mode radios bind OneWay (Browse forces ExplicitPath from the VM side),
    /// so checking one must switch the mode explicitly — same rule as SettingsFileView's
    /// custom-location row; without this the row looks selected while the run keeps the
    /// other mode.
    /// </summary>
    private void OnSettingsAutomaticChecked(object sender, RoutedEventArgs e)
    {
        if (DataContext is LaunchTabViewModel tab)
            tab.Options.SettingsMode = ViewModels.Launch.SettingsSelectionMode.Automatic;
    }

    /// <inheritdoc cref="OnSettingsAutomaticChecked" />
    private void OnSettingsExplicitChecked(object sender, RoutedEventArgs e)
    {
        if (DataContext is LaunchTabViewModel tab)
            tab.Options.SettingsMode = ViewModels.Launch.SettingsSelectionMode.ExplicitPath;
    }

    /// <summary>The change-team action — lands on My teams (navigation is a window concern).</summary>
    private void OnCopyJournal(object sender, RoutedEventArgs e)
    {
        if (DataContext is LaunchTabViewModel { Log.CanCopy: true } tab)
        {
            try
            {
                // SetDataObject(copy: false) skips the flush that makes SetText throw when another
                // process (RDP, clipboard managers, VM tools) is holding the Win32 clipboard open.
                Clipboard.SetDataObject(tab.Log.BuildText(), copy: false);
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                // The clipboard stayed busy: nothing to surface, the button can be pressed again.
            }
        }
    }

    private void OnChangeTeam(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow window)
            window.NavTeams.IsChecked = true;
    }

    /// <summary>The CLI-missing banner's escape hatch: open the Diagnostic screen.</summary>
    private void OnOpenDiagnostic(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow window)
            window.NavDiag.IsChecked = true;
    }
}

// ── L07 (B02): the two lines of a team in the « Équipe » list ──

/// <summary>
/// The title line of a team in the « Équipe » list (B02, R09): its one-line display name, the
/// Markdown stripped (<see cref="TeamCatalog.NormalizeName"/>) — a name typed as a pasted README
/// used to fill the list with « # », « > » and bullets.
/// </summary>
public sealed class TeamChoiceTitleConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is TeamSummary team ? TeamCatalog.NormalizeName(team.Name) : string.Empty;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// The meta line of a team in the « Équipe » list (B02, R19): « 4 agents · lancée le 28/08 » — the
/// agent count when the folder shows one, then the last run (<see cref="TeamLastRunConverter"/>).
/// </summary>
public sealed class TeamChoiceMetaConverter : IValueConverter
{
    private static readonly TeamLastRunConverter LastRun = new();

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not TeamSummary team)
            return string.Empty;

        var lastRun = (string)LastRun.Convert(team.LastRunAt, typeof(string), null, culture);
        return team.AgentCount is { } agents
            ? Services.I18n.Instance["Studio.Run.MetaAgents"].Replace("{0}", agents.ToString(culture), StringComparison.Ordinal) + " · " + lastRun
            : lastRun;
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
