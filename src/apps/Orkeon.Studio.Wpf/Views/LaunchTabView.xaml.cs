using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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

    // ── hrm:L08 — the Run tab ──

    /// <summary>
    /// B09: a copy of the command by selection puts back the plain spaces and drops the word
    /// joiners the display holds, so the clipboard gets exactly the command a terminal takes.
    /// </summary>
    private void OnCommandLineCopying(object sender, DataObjectCopyingEventArgs e)
    {
        if (e.DataObject.GetData(DataFormats.UnicodeText) is not string display)
            return;

        var plain = LaunchTabViewModel.FromNoBreakDisplay(display);
        e.DataObject.SetData(DataFormats.UnicodeText, plain);
        e.DataObject.SetData(DataFormats.Text, plain);
    }

    /// <summary>Narrowest an agent card may get (B04, R15), its 8 px gutter included.</summary>
    private const double AgentCardMinWidth = 180 + 8;

    /// <summary>At most this many agent cards on one row (B04, R15).</summary>
    private const int AgentCardsPerRow = 4;

    /// <summary>
    /// The agent cards wrap (B04, R15): as many columns as 180 px cards fit in the width offered,
    /// four at most, one at least — a five-agent team takes two rows, a narrow pane (the assistant
    /// open) two cards a row rather than five squeezed ones. Layout only.
    /// </summary>
    private void OnAgentCardsSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not UniformGrid grid || !e.WidthChanged)
            return;

        var fit = (int)Math.Floor(e.NewSize.Width / AgentCardMinWidth);
        var columns = Math.Clamp(fit, 1, AgentCardsPerRow);
        if (grid.Columns != columns)
            grid.Columns = columns;
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
