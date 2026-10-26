using System.Windows;
using System.Windows.Controls;

namespace Orkeon.Studio.Wpf.Controls;

/// <summary>
/// The technical block (R18): the engine's own text, as is — mono, selectable, wrapped, on the
/// sunk paper of a well. Never translated, never shown as an interface paragraph: it lives in an
/// <see cref="Alert"/>'s «Technical details» or in a journal. Past <see cref="FrameworkElement.MaxHeight"/>
/// (240 by default) it scrolls inside. Its look is the implicit style in Themes/Components.xaml.
/// </summary>
public sealed class TechBlock : Control
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(TechBlock), new FrameworkPropertyMetadata(""));

    static TechBlock()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(TechBlock), new FrameworkPropertyMetadata(typeof(TechBlock)));
        FocusableProperty.OverrideMetadata(typeof(TechBlock), new FrameworkPropertyMetadata(false));
    }

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
}
