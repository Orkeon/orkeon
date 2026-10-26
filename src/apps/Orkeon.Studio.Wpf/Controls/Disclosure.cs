using System.Windows;
using System.Windows.Controls;

namespace Orkeon.Studio.Wpf.Controls;

/// <summary>
/// The one fold of Studio (R16): a ghost button «label + chevron» — chevron down while closed,
/// up while open — over the folded Content, which is collapsed (neither drawn nor measured)
/// while <see cref="IsExpanded"/> is false. The label is the Header and stays the same in both
/// states («Technical details», «Advanced options», «Technical journal», «For every account»):
/// never an eye, never «Show / hide». Its look is the implicit style in Themes/Components.xaml.
/// </summary>
public sealed class Disclosure : HeaderedContentControl
{
    public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(
        nameof(IsExpanded), typeof(bool), typeof(Disclosure),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    static Disclosure()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(Disclosure), new FrameworkPropertyMetadata(typeof(Disclosure)));
        FocusableProperty.OverrideMetadata(typeof(Disclosure), new FrameworkPropertyMetadata(false));
    }

    public bool IsExpanded { get => (bool)GetValue(IsExpandedProperty); set => SetValue(IsExpandedProperty, value); }
}
