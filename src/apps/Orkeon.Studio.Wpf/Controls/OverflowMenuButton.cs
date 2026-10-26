using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Markup;

namespace Orkeon.Studio.Wpf.Controls;

/// <summary>
/// The «…» menu (R23): a 32 px square button with the Lucide <c>ellipsis</c> (more-horizontal),
/// always the last of its row, that opens under itself, right edges aligned, a menu of the items
/// the caller gives (its XAML content: <see cref="MenuItem"/>s and <see cref="Separator"/>s).
/// An item marked <see cref="SetIsDestructive">IsDestructive</see> (Supprimer) is drawn in the
/// danger red, and a rule is put before the first one when the caller did not. The items take
/// the button's DataContext, so <c>Command="{Binding ExportCommand}"</c> works as on the button
/// itself; an ElementName binding does not reach into the menu. Its look is the implicit style in
/// Themes/Components.xaml (IconBtn).
/// </summary>
[ContentProperty(nameof(Items))]
public sealed class OverflowMenuButton : Button
{
    public static readonly DependencyProperty IsDestructiveProperty = DependencyProperty.RegisterAttached(
        "IsDestructive", typeof(bool), typeof(OverflowMenuButton), new FrameworkPropertyMetadata(false));

    private readonly ContextMenu _menu;

    static OverflowMenuButton()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(OverflowMenuButton), new FrameworkPropertyMetadata(typeof(OverflowMenuButton)));
    }

    public OverflowMenuButton()
    {
        _menu = new ContextMenu
        {
            PlacementTarget = this,
            Placement = PlacementMode.Custom,
            CustomPopupPlacementCallback = UnderRightEdge,
        };
        _menu.SetBinding(DataContextProperty, new Binding(nameof(DataContext)) { Source = this });
        Content = new LucideIcon { Kind = "ellipsis", Size = 16 };
        SetBinding(ToolTipProperty, new Binding("[Studio.Shell.MoreActions]") { Source = Services.I18n.Instance });
        SetBinding(AutomationProperties.NameProperty, new Binding("[Studio.Shell.MoreActions]") { Source = Services.I18n.Instance });
    }

    /// <summary>Raised just before the menu opens: the last moment to fill or refresh <see cref="Items"/>.</summary>
    public event EventHandler? MenuOpening;

    /// <summary>The items of the menu, in their order; the XAML content of the button.</summary>
    public ItemCollection Items => _menu.Items;

    public static bool GetIsDestructive(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(IsDestructiveProperty);
    }

    public static void SetIsDestructive(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsDestructiveProperty, value);
    }

    protected override void OnClick()
    {
        base.OnClick();
        MenuOpening?.Invoke(this, EventArgs.Empty);
        Dress(_menu);
        if (_menu.Items.Count > 0)
        {
            _menu.IsOpen = true;
        }
    }

    /// <summary>
    /// The danger red on every destructive item, and one rule before the first of them when an
    /// item precedes it and is not already a rule.
    /// </summary>
    internal static void Dress(ItemsControl menu)
    {
        var first = -1;
        for (var i = 0; i < menu.Items.Count; i++)
        {
            if (menu.Items[i] is MenuItem item && GetIsDestructive(item))
            {
                item.SetResourceReference(ForegroundProperty, "DangerBrush");
                if (first < 0)
                {
                    first = i;
                }
            }
        }

        if (first > 0 && menu.Items[first - 1] is not Separator)
        {
            menu.Items.Insert(first, new Separator());
        }
    }

    // Under the button, the menu's right edge on the button's; above it when the screen is short.
    private static CustomPopupPlacement[] UnderRightEdge(Size popupSize, Size targetSize, Point offset) =>
    [
        new(new Point(targetSize.Width - popupSize.Width, targetSize.Height + 4), PopupPrimaryAxis.Horizontal),
        new(new Point(targetSize.Width - popupSize.Width, -popupSize.Height - 4), PopupPrimaryAxis.Horizontal),
    ];
}
