using System.Windows;
using Orkeon.Studio.Wpf.ViewModels.Shell;
using Orkeon.Studio.Wpf.ViewModels.Teams;

namespace Orkeon.Studio.Wpf.Views;

/// <summary>The "Importer" screen: the drop zone and the expert "test it first" hop.</summary>
public partial class ImportTeamView : System.Windows.Controls.UserControl
{
    public ImportTeamView() => InitializeComponent();

    private void OnDragOverCandidate(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDropCandidate(object sender, DragEventArgs e)
    {
        if (DataContext is ImportTeamViewModel vm
            && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths)
        {
            // Select() both sets the path and runs the detection — a drop is a full pick.
            vm.Target.Select(paths[0]);
        }
    }

    private void OnTestFirst(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow window
            && window.DataContext is MainWindowViewModel shell
            && DataContext is ImportTeamViewModel vm
            && vm.Target.SelectedPath is { Length: > 0 } path)
        {
            shell.Test.Launcher.Target.Select(path);
            window.NavTest.IsChecked = true;
        }
    }
}
