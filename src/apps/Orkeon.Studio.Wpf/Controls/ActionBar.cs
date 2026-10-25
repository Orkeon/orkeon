using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Orkeon.Studio.Wpf.Controls;

/// <summary>
/// The action bar of an object (R5): one line, never wrapped. The buttons are laid left to
/// right in the order given; those that no longer fit — the right-hand ones — leave the line and
/// are offered by a «…» button at its end, which opens them as a menu. A
/// <see cref="Separator"/> (or a <c>VSeparator</c> rectangle) between two buttons draws the vertical rule between the groups
/// (travailler · fichiers · cycle de vie); a rule left at the edge of the line is not drawn.
/// A hidden button is not removed from the tree: it keeps its bindings, its command and its
/// CanExecute, and the menu reads them at the moment it opens.
/// </summary>
public sealed class ActionBar : Panel
{
    private const double Gap = 8;

    private readonly Button _more;
    private readonly List<UIElement> _hidden = [];

    public ActionBar()
    {
        _more = new Button
        {
            Content = new LucideIcon { Kind = "ellipsis", Size = 16 },
            Width = 40,
        };
        _more.SetResourceReference(StyleProperty, "BtnSecondary");
        _more.SetBinding(ToolTipProperty, new System.Windows.Data.Binding("[Studio.Shell.MoreActions]") { Source = Services.I18n.Instance });
        _more.SetBinding(AutomationProperties.NameProperty, new System.Windows.Data.Binding("[Studio.Shell.MoreActions]") { Source = Services.I18n.Instance });
        _more.Click += (_, _) => OpenMenu();
        AddVisualChild(_more);
        AddLogicalChild(_more);
    }

    /// <summary>The height of the bar's own «…» button; the buttons inside carry their own.</summary>
    public double ButtonHeight
    {
        get => _more.Height;
        set => _more.Height = value;
    }

    protected override int VisualChildrenCount => base.VisualChildrenCount + 1;

    protected override Visual GetVisualChild(int index) =>
        index == base.VisualChildrenCount ? _more : base.GetVisualChild(index);

    protected override Size MeasureOverride(Size availableSize)
    {
        _more.Measure(new Size(double.PositiveInfinity, availableSize.Height));
        double total = 0, height = _more.DesiredSize.Height;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            if (child.Visibility != Visibility.Collapsed)
            {
                total += child.DesiredSize.Width + Gap;
                height = Math.Max(height, child.DesiredSize.Height);
            }
        }

        var width = double.IsInfinity(availableSize.Width) ? total : Math.Min(total, availableSize.Width);
        return new Size(Math.Max(0, width - Gap), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var visible = InternalChildren.Cast<UIElement>().Where(c => c.Visibility != Visibility.Collapsed).ToList();
        var needed = visible.Sum(c => c.DesiredSize.Width + Gap) - Gap;
        var budget = needed <= finalSize.Width ? finalSize.Width : finalSize.Width - _more.DesiredSize.Width - Gap;

        _hidden.Clear();
        var shown = new List<UIElement>();
        double used = 0;
        foreach (var child in visible)
        {
            var width = child.DesiredSize.Width;
            if (_hidden.Count == 0 && used + width <= budget)
            {
                shown.Add(child);
                used += width + Gap;
            }
            else
            {
                _hidden.Add(child);
            }
        }

        // A rule is only drawn between two shown buttons.
        while (shown.Count > 0 && shown[^1] is Separator or System.Windows.Shapes.Rectangle)
        {
            _hidden.Insert(0, shown[^1]);
            shown.RemoveAt(shown.Count - 1);
        }

        double x = 0;
        foreach (var child in shown)
        {
            var y = (finalSize.Height - child.DesiredSize.Height) / 2;
            child.Arrange(new Rect(x, y, child.DesiredSize.Width, child.DesiredSize.Height));
            child.Clip = null;
            child.IsHitTestVisible = true;
            x += child.DesiredSize.Width + Gap;
        }

        foreach (var child in _hidden)
        {
            child.Arrange(new Rect(0, 0, 0, 0));
            child.Clip = Geometry.Empty;
            child.IsHitTestVisible = false;
        }

        foreach (var child in InternalChildren.Cast<UIElement>().Where(c => c.Visibility == Visibility.Collapsed))
        {
            child.Arrange(new Rect(0, 0, 0, 0));
        }

        var showMore = _hidden.Any(c => c is ButtonBase);
        _more.Visibility = showMore ? Visibility.Visible : Visibility.Hidden;
        _more.Arrange(showMore
            ? new Rect(x, (finalSize.Height - _more.DesiredSize.Height) / 2, _more.DesiredSize.Width, _more.DesiredSize.Height)
            : new Rect(0, 0, 0, 0));

        return finalSize;
    }

    private void OpenMenu()
    {
        var menu = new ContextMenu { PlacementTarget = _more, Placement = PlacementMode.Bottom };
        foreach (var button in _hidden.OfType<ButtonBase>())
        {
            var item = new MenuItem
            {
                Header = LabelOf(button),
                IsEnabled = button.IsEnabled,
                ToolTip = button.ToolTip,
            };
            ToolTipService.SetShowOnDisabled(item, true);
            var target = button;
            item.Click += (_, _) => Invoke(target);
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    private static void Invoke(ButtonBase button)
    {
        // The automation peer runs the button's own OnClick: its Click handlers and its command.
        if (UIElementAutomationPeer.CreatePeerForElement(button).GetPattern(PatternInterface.Invoke) is IInvokeProvider invoke)
        {
            invoke.Invoke();
        }
    }

    private static string LabelOf(ButtonBase button)
    {
        var name = AutomationProperties.GetName(button);
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        return button.Content switch
        {
            string text => text,
            DependencyObject tree => FirstText(tree) ?? "",
            _ => "",
        };
    }

    private static string? FirstText(DependencyObject node)
    {
        if (node is TextBlock { Text.Length: > 0 } block)
        {
            return block.Text;
        }

        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
        {
            if (FirstText(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
