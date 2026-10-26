using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Orkeon.Studio.Wpf.Controls;

/// <summary>
/// The action bar of a card (R23): one line, never wrapped, in the order given — the one order of
/// both modes: Lancer, Essayer | Modifier, Renommer | Ouvrir le dossier, Dupliquer | «…». Novice
/// collapses the buttons it does not show (Visibility), it never reorders them. A child marked
/// <see cref="SetInMenu">ActionBar.InMenu</see> (Exporter, Archiver, Supprimer) never sits on the
/// line: it is offered by the «…» button, a 32 px square (<see cref="OverflowMenuButton"/>) always
/// at the end of the line. The buttons that no longer fit — the right-hand ones — join the menu
/// too, before the InMenu ones, in the order given. A destructive item (a BtnDanger button, or one
/// marked OverflowMenuButton.IsDestructive) closes the menu in red after a rule.
/// A <see cref="Separator"/> or a rectangle (the <c>ActionBarRule</c> style: 1 px, FieldBorderBrush)
/// between two buttons draws the vertical rule between the groups; a rule at either edge of the
/// line, or right after another one (a group Novice emptied), is not drawn.
/// A hidden button is not removed from the tree: it keeps its bindings, its command and its
/// CanExecute, and the menu reads them at the moment it opens.
/// </summary>
public sealed class ActionBar : Panel
{
    private const double Gap = 8;

    public static readonly DependencyProperty InMenuProperty = DependencyProperty.RegisterAttached(
        "InMenu", typeof(bool), typeof(ActionBar),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure | FrameworkPropertyMetadataOptions.AffectsParentArrange));

    private readonly OverflowMenuButton _more;
    private readonly List<UIElement> _hidden = [];
    private readonly HashSet<Control> _untabbed = [];

    public ActionBar()
    {
        _more = new OverflowMenuButton();
        _more.MenuOpening += (_, _) => FillMenu();
        AddVisualChild(_more);
        AddLogicalChild(_more);
    }

    /// <summary>The side of the bar's own square «…» button (32 by default); the buttons inside carry their own height.</summary>
    public double ButtonHeight
    {
        get => _more.Height;
        set
        {
            _more.Height = value;
            _more.Width = value;
        }
    }

    public static bool GetInMenu(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(InMenuProperty);
    }

    public static void SetInMenu(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(InMenuProperty, value);
    }

    protected override int VisualChildrenCount => base.VisualChildrenCount + 1;

    protected override Visual GetVisualChild(int index) =>
        index == base.VisualChildrenCount ? _more : base.GetVisualChild(index);

    private static bool IsRule(UIElement child) => child is Separator or System.Windows.Shapes.Rectangle;

    private bool HasMenuOnlyButton() =>
        InternalChildren.Cast<UIElement>().Any(c => c.Visibility != Visibility.Collapsed && c is ButtonBase && GetInMenu(c));

    protected override Size MeasureOverride(Size availableSize)
    {
        _more.Measure(new Size(double.PositiveInfinity, availableSize.Height));
        double total = 0, height = _more.DesiredSize.Height;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            if (child.Visibility != Visibility.Collapsed && !GetInMenu(child))
            {
                total += child.DesiredSize.Width + Gap;
                height = Math.Max(height, child.DesiredSize.Height);
            }
        }

        if (HasMenuOnlyButton())
        {
            total += _more.DesiredSize.Width + Gap;
        }

        var width = double.IsInfinity(availableSize.Width) ? total : Math.Min(total, availableSize.Width);
        return new Size(Math.Max(0, width - Gap), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var visible = InternalChildren.Cast<UIElement>().Where(c => c.Visibility != Visibility.Collapsed).ToList();
        var onLine = visible.Where(c => !GetInMenu(c)).ToList();
        var needed = onLine.Sum(c => c.DesiredSize.Width + Gap) - Gap;
        var budget = needed <= finalSize.Width && !HasMenuOnlyButton()
            ? finalSize.Width
            : finalSize.Width - _more.DesiredSize.Width - Gap;

        var shown = new List<UIElement>();
        var overflowed = false;
        double used = 0;
        foreach (var child in onLine)
        {
            var width = child.DesiredSize.Width;
            if (!overflowed && used + width <= budget)
            {
                shown.Add(child);
                used += width + Gap;
            }
            else
            {
                overflowed = true;
            }
        }

        // A rule is only drawn between two shown buttons: never at an edge, never doubled.
        for (var i = shown.Count - 1; i >= 0; i--)
        {
            if (IsRule(shown[i]) && (i == 0 || i == shown.Count - 1 || IsRule(shown[i - 1])))
            {
                shown.RemoveAt(i);
            }
        }

        // The menu takes everything that is not on the line, in the order given.
        _hidden.Clear();
        _hidden.AddRange(visible.Where(c => !shown.Contains(c)));

        double x = 0;
        foreach (var child in shown)
        {
            var y = (finalSize.Height - child.DesiredSize.Height) / 2;
            child.Arrange(new Rect(x, y, child.DesiredSize.Width, child.DesiredSize.Height));
            child.Clip = null;
            child.IsHitTestVisible = true;
            if (child is Control shownControl && _untabbed.Remove(shownControl))
            {
                shownControl.SetCurrentValue(Control.IsTabStopProperty, true);
            }

            x += child.DesiredSize.Width + Gap;
        }

        foreach (var child in _hidden)
        {
            child.Arrange(new Rect(0, 0, 0, 0));
            child.Clip = Geometry.Empty;
            child.IsHitTestVisible = false;
            // A button gone to the menu is reached from the menu, never by Tab on an empty spot.
            if (child is Control { IsTabStop: true } hiddenControl)
            {
                hiddenControl.SetCurrentValue(Control.IsTabStopProperty, false);
                _untabbed.Add(hiddenControl);
            }
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

    private void FillMenu()
    {
        _more.Items.Clear();
        foreach (var button in _hidden.OfType<ButtonBase>())
        {
            var item = new MenuItem
            {
                Header = LabelOf(button),
                IsEnabled = button.IsEnabled,
                ToolTip = button.ToolTip,
            };
            if (FirstIcon(button) is { } icon)
            {
                item.Icon = new LucideIcon { Kind = icon.Kind, Size = 16 };
            }

            // A destructive action keeps its colour in the menu, after a rule (R06, R23).
            if (OverflowMenuButton.GetIsDestructive(button) || ReferenceEquals(button.Style, button.TryFindResource("BtnDanger")))
            {
                OverflowMenuButton.SetIsDestructive(item, true);
            }

            ToolTipService.SetShowOnDisabled(item, true);
            var target = button;
            item.Click += (_, _) => Invoke(target);
            _more.Items.Add(item);
        }
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

    private static LucideIcon? FirstIcon(DependencyObject node)
    {
        if (node is LucideIcon icon)
        {
            return icon;
        }

        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
        {
            if (FirstIcon(child) is { } found)
            {
                return found;
            }
        }

        return null;
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
