using System.Windows;
using System.Windows.Controls;
using Orkeon.Studio.Wpf.ViewModels.Mounts;

namespace Orkeon.Studio.Wpf.Views;

/// <summary>The mount editor pane. XAML wiring only; every behaviour lives in the ViewModel.</summary>
public partial class MountsEditorView : UserControl
{
    /// <summary>Loads the XAML.</summary>
    public MountsEditorView() => InitializeComponent();

    /// <summary>Routes a suggestion chip to the selected mount's virtual path (chips carry no command).</summary>
    private void OnSuggestVirtualPath(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string path }
            && DataContext is MountsEditorViewModel { SelectedMount: { } mount })
        {
            mount.VirtualPath = path;
        }
    }
}
