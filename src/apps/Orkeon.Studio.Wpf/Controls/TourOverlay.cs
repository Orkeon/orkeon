using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Orkeon.Studio.Wpf.Services;
using Orkeon.Studio.Wpf.Services.Capture;

namespace Orkeon.Studio.Wpf.Controls;

/// <summary>One step of the guided tour.</summary>
/// <param name="TargetName">x:Name of the element to spotlight; null = centered welcome step.</param>
/// <param name="TitleKey">resx key of the title.</param>
/// <param name="BodyKey">resx key of the body.</param>
/// <param name="Prepare">Optional navigation to run before measuring (e.g. select a nav item).</param>
public sealed record TourStep(string? TargetName, string TitleKey, string BodyKey, Action? Prepare = null);

/// <summary>
/// Spotlight guided tour: dims the window, cuts a rounded hole over the target,
/// and shows a popover card with step dots and Skip / Back / Next.
/// Add as the LAST child of the window's root Grid (spanning all rows/columns),
/// then call Start(steps).
/// </summary>
public sealed class TourOverlay : Grid
{
    private const double Pad = 6, PopW = 340, Radius = 12;

    private IReadOnlyList<TourStep> _steps = [];
    private int _index;

    private readonly Path _dim = new();
    private readonly Rectangle _ring = new()
    {
        RadiusX = Radius, RadiusY = Radius, StrokeThickness = 2,
        Fill = Brushes.Transparent, IsHitTestVisible = false,
    };
    private readonly Border _pop = new() { Width = PopW, CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1) };
    private readonly TextBlock _title = new() { FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _body = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, LineHeight = 20, Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock _counter = new() { FontSize = 10.5 };
    private readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
    private readonly Button _skip = new(), _back = new(), _next = new();
    private readonly Canvas _canvas = new();

    public TourOverlay()
    {
        Visibility = Visibility.Collapsed;
        _canvas.Children.Add(_dim);
        _canvas.Children.Add(_ring);
        Children.Add(_canvas);

        var head = new DockPanel();
        var label = new TextBlock { FontSize = 10 };
        label.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        label.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("[Studio.Shell.TourLabel]") { Source = I18n.Instance });
        _counter.Margin = new Thickness(9, 1, 0, 0);
        head.Children.Add(label);
        head.Children.Add(_counter);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        Wire(_skip, "Studio.Shell.TourSkip", (_, _) => End());
        Wire(_back, "Studio.Shell.Back", (_, _) => Go(_index - 1));
        Wire(_next, "Studio.Shell.Next", (_, _) => { if (_index >= _steps.Count - 1) End(); else Go(_index + 1); });
        _back.Margin = _next.Margin = new Thickness(7, 0, 0, 0);
        buttons.Children.Add(_skip);
        buttons.Children.Add(_back);
        buttons.Children.Add(_next);

        var stack = new StackPanel();
        _title.Margin = new Thickness(0, 9, 0, 0);
        stack.Children.Add(head);
        stack.Children.Add(_title);
        stack.Children.Add(_body);
        stack.Children.Add(_dots);
        stack.Children.Add(buttons);
        _pop.Child = stack;
        _pop.Padding = new Thickness(18, 16, 18, 16);
        _canvas.Children.Add(_pop);

        // theme-aware brushes
        _dim.SetResourceReference(Shape.FillProperty, "TourScrimBrush");
        _pop.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        _pop.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        _ring.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
        label.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
        _counter.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
        _counter.SetResourceReference(TextBlock.ForegroundProperty, "InkFaintBrush");
        _title.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        _title.SetResourceReference(TextBlock.ForegroundProperty, "InkBrush");
        _body.SetResourceReference(TextBlock.ForegroundProperty, "InkMutedBrush");

        SizeChanged += (_, _) => { if (Visibility == Visibility.Visible) Layout(); };
    }

    private void Wire(Button b, string key, RoutedEventHandler click)
    {
        b.SetResourceReference(StyleProperty, key == "Studio.Shell.Next" ? "BtnPrimary" : "BtnGhost");
        b.FontSize = 12;
        b.Padding = new Thickness(10, 6, 10, 6);
        b.SetBinding(ContentControl.ContentProperty, new System.Windows.Data.Binding("[" + key + "]") { Source = I18n.Instance });
        b.Click += click;
        if (key == "Studio.Shell.Next") _nextKeyHolder = b;
    }

    private Button? _nextKeyHolder;

    /// <summary>
    /// Shows the tour from <paramref name="startIndex"/>. The index is a parameter rather than
    /// always zero because the screenshot campaign photographs every stop: without it only the
    /// first of the five is reachable from outside.
    /// </summary>
    public void Start(IReadOnlyList<TourStep> steps, int startIndex = 0)
    {
        _steps = steps;
        Visibility = Visibility.Visible;
        Go(startIndex);
    }

    public void End()
    {
        Visibility = Visibility.Collapsed;
    }

    private void Go(int i)
    {
        if (i < 0 || i >= _steps.Count) { End(); return; }
        _index = i;
        _steps[i].Prepare?.Invoke();
        // let the prepared screen render before measuring the target
        Dispatcher.BeginInvoke(Layout, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1863",
        Justification = "The format string is the CURRENT culture's: caching a CompositeFormat "
                      + "here would keep rendering the previous language's shape after a hot switch, "
                      + "which is the one thing this counter must not do.")]
    private void Layout()
    {
        var step = _steps[_index];
        var spotlight = MeasureSpotlight(step);

        ApplySpotlight(spotlight);

        _title.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("[" + step.TitleKey + "]") { Source = I18n.Instance });
        _body.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("[" + step.BodyKey + "]") { Source = I18n.Instance });
        // A counter is a sentence too: the joiner is catalogued rather than welded in,
        // like every other assembled label (T-15). CompositeFormat is deliberately NOT
        // cached: the pattern changes with the language, and a cached one would keep
        // rendering the previous culture's shape after a hot switch.
        _counter.Text = string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
            I18n.T("Studio.Shell.CounterPattern"), _index + 1, _steps.Count);
        _back.Visibility = _index > 0 ? Visibility.Visible : Visibility.Collapsed;
        _nextKeyHolder?.SetBinding(ContentControl.ContentProperty,
            new System.Windows.Data.Binding("[" + (_index == _steps.Count - 1 ? "Studio.Shell.Finish" : "Studio.Shell.Next") + "]") { Source = I18n.Instance });

        RebuildDots();

        _pop.Measure(new Size(PopW, double.PositiveInfinity));
        var ph = Math.Max(_pop.DesiredSize.Height, 200);
        var (x, y) = PlacePopover(spotlight, ph);
        Canvas.SetLeft(_pop, x); Canvas.SetTop(_pop, y);
    }

    /// <summary>
    /// The rectangle to spotlight for this step, padded around the target element; null when the
    /// step names no target, or names one that is not on screen -- the centered welcome case.
    /// </summary>
    private Rect? MeasureSpotlight(TourStep step)
    {
        if (step.TargetName is null)
            return null;

        var el = ResolveTarget(step.TargetName);
        if (el is not { IsVisible: true })
            return null;

        var origin = el.TransformToVisual(this).Transform(new Point(0, 0));
        return new Rect(origin.X - Pad, origin.Y - Pad, el.ActualWidth + 2 * Pad, el.ActualHeight + 2 * Pad);
    }

    /// <summary>Dims the whole window and, when there is a target, cuts the hole and rings it.</summary>
    private void ApplySpotlight(Rect? spotlight)
    {
        var full = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight));

        if (spotlight is { } rect)
        {
            _dim.Data = new CombinedGeometry(GeometryCombineMode.Exclude, full,
                new RectangleGeometry(rect, Radius, Radius));
            _ring.Visibility = Visibility.Visible;
            _ring.Width = rect.Width; _ring.Height = rect.Height;
            Canvas.SetLeft(_ring, rect.X); Canvas.SetTop(_ring, rect.Y);
            return;
        }

        _dim.Data = full;
        _ring.Visibility = Visibility.Collapsed;
    }

    /// <summary>One clickable dot per stop, the current one accented.</summary>
    private void RebuildDots()
    {
        _dots.Children.Clear();
        for (var j = 0; j < _steps.Count; j++)
        {
            var dot = new Ellipse { Width = 7, Height = 7, Margin = new Thickness(2, 0, 2, 0), Cursor = System.Windows.Input.Cursors.Hand };
            dot.SetResourceReference(Shape.FillProperty, j == _index ? "AccentBrush" : "LineStrongBrush");
            var target = j;
            dot.MouseLeftButtonUp += (_, _) => Go(target);
            _dots.Children.Add(dot);
        }
    }

    /// <summary>Popover placement: below the target, above it, beside it, else centered.</summary>
    private (double X, double Y) PlacePopover(Rect? spotlight, double ph)
    {
        (double X, double Y) centered = ((ActualWidth - PopW) / 2, (ActualHeight - ph) / 2);

        if (spotlight is null)
            return centered;

        var t = spotlight.Value;

        if (ActualHeight - t.Bottom - 14 >= ph)
            return (Clamp(t.X, 12, ActualWidth - PopW - 12), t.Bottom + 14);

        if (t.Y - 14 >= ph)
            return (Clamp(t.X, 12, ActualWidth - PopW - 12), t.Y - ph - 14);

        if (ActualWidth - t.Right - 18 >= PopW + 12)
            return (t.Right + 18, Clamp(t.Y + 8, 12, ActualHeight - ph - 12));

        if (t.X - 18 >= PopW + 12)
            return (t.X - PopW - 18, Clamp(t.Y + 8, 12, ActualHeight - ph - 12));

        return centered;
    }

    private static double Clamp(double v, double min, double max) => Math.Max(min, Math.Min(v, max));

    /// <summary>
    /// Finds a step target by name. Window.FindName alone cannot see names registered in a
    /// UserControl's own namescope, so a visual-tree walk is the fallback that keeps every
    /// spotlight working whatever view declares the anchor.
    /// </summary>
    private FrameworkElement? ResolveTarget(string name)
    {
        var window = Window.GetWindow(this);
        if (window is null)
            return null;

        if (window.FindName(name) is FrameworkElement direct)
            return direct;

        return CaptureVisualTree.FindByName(window, name);
    }
}
