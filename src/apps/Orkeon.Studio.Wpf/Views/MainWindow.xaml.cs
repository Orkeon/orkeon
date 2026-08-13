using System.Windows;

namespace Orkeon.Studio.Wpf.Views;

/// <summary>
/// The two-tab window. The code-behind is the XAML wiring and nothing else: the DataContext is set
/// by the caller and every behaviour lives in the ViewModel.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>Loads the XAML.</summary>
    public MainWindow() => InitializeComponent();
}
