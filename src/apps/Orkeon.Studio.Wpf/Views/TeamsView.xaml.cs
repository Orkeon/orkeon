using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Orkeon.Studio.Wpf.Views;

/// <summary>The my-teams screen. XAML wiring only; every behaviour lives in the ViewModel.</summary>
public partial class TeamsView : UserControl
{
    /// <summary>Loads the XAML.</summary>
    public TeamsView() => InitializeComponent();

    /// <summary>
    /// R24 (D11): a card's in-place editor — the delete confirmation, the rename field — opens where
    /// the action bar was, possibly under the fold: once laid out, it is scrolled into view. Only the
    /// scroll position changes; the ViewModel knows nothing of it.
    /// </summary>
    private void OnEditorShown(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true || sender is not FrameworkElement editor)
            return;

        _ = editor.Dispatcher.InvokeAsync(editor.BringIntoView, DispatcherPriority.Loaded);
    }
}
