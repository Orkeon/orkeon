using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Orkeon.Studio.Wpf.Controls;

/// <summary>
/// Renders a Lucide icon from the geometries in Themes/Icons.xaml
/// (keys "Lucide.&lt;kebab-name&gt;"), stroked with Foreground, 2px round caps —
/// the same rendering as the web design's &lt;l-i&gt; element.
/// Usage: &lt;controls:LucideIcon Kind="play" Size="14"/&gt;
/// </summary>
public sealed class LucideIcon : Control
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(string), typeof(LucideIcon),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((LucideIcon)d).Rebuild()));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(LucideIcon),
        new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure, (d, _) => ((LucideIcon)d).Rebuild()));

    private readonly Viewbox _box = new();
    private readonly Path _path = new()
    {
        Width = 24, Height = 24, Stretch = Stretch.None,
        StrokeThickness = 2, Fill = null,
        StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,
    };

    public LucideIcon()
    {
        _box.Child = _path;
        AddVisualChild(_box);
        _path.SetBinding(Shape.StrokeProperty,
            new System.Windows.Data.Binding(nameof(Foreground)) { Source = this });
        Loaded += (_, _) => Rebuild();
    }

    public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public double Size { get => (double)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => _box;

    protected override System.Windows.Size MeasureOverride(System.Windows.Size constraint)
    {
        _box.Measure(new System.Windows.Size(Size, Size));
        return new System.Windows.Size(Size, Size);
    }

    protected override System.Windows.Size ArrangeOverride(System.Windows.Size arrangeBounds)
    {
        _box.Arrange(new Rect(0, 0, Size, Size));
        return new System.Windows.Size(Size, Size);
    }

    private void Rebuild()
    {
        _box.Width = Size;
        _box.Height = Size;
        _path.Data = string.IsNullOrEmpty(Kind) ? null : TryFindResource("Lucide." + Kind) as Geometry;
        InvalidateMeasure();
    }
}
