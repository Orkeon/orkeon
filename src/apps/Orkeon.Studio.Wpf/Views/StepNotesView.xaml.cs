using System.Windows;
using System.Windows.Controls;

namespace Orkeon.Studio.Wpf.Views;

/// <summary>The per-step consigne block. XAML wiring plus its one Title dependency property.</summary>
public partial class StepNotesView : UserControl
{
    /// <summary>The step's title ("Consigne de composition", …), set by the host.</summary>
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(StepNotesView), new PropertyMetadata(""));

    /// <summary>Loads the XAML.</summary>
    public StepNotesView() => InitializeComponent();

    /// <summary>See <see cref="TitleProperty"/>.</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }
}
