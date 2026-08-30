using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Orkeon.Studio.Wpf.Services;

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
        label.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("[Tour_Label]") { Source = I18n.Instance });
        _counter.Margin = new Thickness(9, 1, 0, 0);
        head.Children.Add(label);
        head.Children.Add(_counter);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        Wire(_skip, "Tour_Skip", (_, _) => End());
        Wire(_back, "Tour_Back", (_, _) => Go(_index - 1));
        Wire(_next, "Tour_Next", (_, _) => { if (_index >= _steps.Count - 1) End(); else Go(_index + 1); });
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
        b.SetResourceReference(StyleProperty, key == "Tour_Next" ? "BtnPrimary" : "BtnGhost");
        b.FontSize = 12;
        b.Padding = new Thickness(10, 6, 10, 6);
        b.SetBinding(ContentControl.ContentProperty, new System.Windows.Data.Binding("[" + key + "]") { Source = I18n.Instance });
        b.Click += click;
        if (key == "Tour_Next") _nextKeyHolder = b;
    }

    private Button? _nextKeyHolder;

    public void Start(IReadOnlyList<TourStep> steps)
    {
        _steps = steps;
        Visibility = Visibility.Visible;
        Go(0);
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

    private void Layout()
    {
        var step = _steps[_index];
        var full = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight));

        Rect? r = null;
        if (step.TargetName is not null
            && ResolveTarget(step.TargetName) is { IsVisible: true } el)
        {
            var origin = el.TransformToVisual(this).Transform(new Point(0, 0));
            r = new Rect(origin.X - Pad, origin.Y - Pad, el.ActualWidth + 2 * Pad, el.ActualHeight + 2 * Pad);
        }

        if (r is { } rect)
        {
            _dim.Data = new CombinedGeometry(GeometryCombineMode.Exclude, full,
                new RectangleGeometry(rect, Radius, Radius));
            _ring.Visibility = Visibility.Visible;
            _ring.Width = rect.Width; _ring.Height = rect.Height;
            Canvas.SetLeft(_ring, rect.X); Canvas.SetTop(_ring, rect.Y);
        }
        else
        {
            _dim.Data = full;
            _ring.Visibility = Visibility.Collapsed;
        }

        _title.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("[" + step.TitleKey + "]") { Source = I18n.Instance });
        _body.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("[" + step.BodyKey + "]") { Source = I18n.Instance });
        // A counter is a sentence too: the joiner is catalogued rather than welded in,
        // like every other assembled label (T-15).
        _counter.Text = string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
            I18n.T("Vm_Tour_CounterPattern"), _index + 1, _steps.Count);
        _back.Visibility = _index > 0 ? Visibility.Visible : Visibility.Collapsed;
        _nextKeyHolder?.SetBinding(ContentControl.ContentProperty,
            new System.Windows.Data.Binding("[" + (_index == _steps.Count - 1 ? "Tour_Finish" : "Tour_Next") + "]") { Source = I18n.Instance });

        _dots.Children.Clear();
        for (var j = 0; j < _steps.Count; j++)
        {
            var dot = new Ellipse { Width = 7, Height = 7, Margin = new Thickness(2, 0, 2, 0), Cursor = System.Windows.Input.Cursors.Hand };
            dot.SetResourceReference(Shape.FillProperty, j == _index ? "AccentBrush" : "LineStrongBrush");
            var target = j;
            dot.MouseLeftButtonUp += (_, _) => Go(target);
            _dots.Children.Add(dot);
        }

        // popover placement: below the target, above it, beside it, else centered
        _pop.Measure(new Size(PopW, double.PositiveInfinity));
        var ph = Math.Max(_pop.DesiredSize.Height, 200);
        double x, y;
        if (r is { } t)
        {
            if (ActualHeight - t.Bottom - 14 >= ph) { x = Clamp(t.X, 12, ActualWidth - PopW - 12); y = t.Bottom + 14; }
            else if (t.Y - 14 >= ph) { x = Clamp(t.X, 12, ActualWidth - PopW - 12); y = t.Y - ph - 14; }
            else if (ActualWidth - t.Right - 18 >= PopW + 12) { x = t.Right + 18; y = Clamp(t.Y + 8, 12, ActualHeight - ph - 12); }
            else if (t.X - 18 >= PopW + 12) { x = t.X - PopW - 18; y = Clamp(t.Y + 8, 12, ActualHeight - ph - 12); }
            else { x = (ActualWidth - PopW) / 2; y = (ActualHeight - ph) / 2; }
        }
        else
        {
            x = (ActualWidth - PopW) / 2; y = (ActualHeight - ph) / 2;
        }
        Canvas.SetLeft(_pop, x); Canvas.SetTop(_pop, y);
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

        return FindByName(window, name);
    }

    private static FrameworkElement? FindByName(DependencyObject root, string name)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement fe && fe.Name == name)
                return fe;

            if (FindByName(child, name) is { } nested)
                return nested;
        }

        return null;
    }
}
