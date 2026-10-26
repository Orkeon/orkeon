using System.Windows;

namespace Orkeon.Studio.Wpf.Controls;

/// <summary>Where <see cref="ButtonIcon"/> draws its icon against the label.</summary>
public enum ButtonIconPlacement
{
    /// <summary>Left of the label: every action, «back» (←) included (R05).</summary>
    Leading,

    /// <summary>Right of the label: «forward» only (→, R05).</summary>
    Trailing,
}

/// <summary>
/// The icon of a button (hrm R05): a 16 px Lucide icon beside the label, stroked with the
/// button's own Foreground, 8 px from the text. A view says
/// <c>controls:ButtonIcon.Kind="plus"</c> on any button whose style derives from <c>BtnBase</c>
/// and keeps a plain string as its Content; <c>controls:ButtonIcon.Placement="Trailing"</c> puts
/// a forward arrow after the label. With no Content the icon stands alone, centred (icon-only
/// buttons, <c>IconBtn</c>). The template reads <see cref="HasLeadingProperty"/> and
/// <see cref="HasTrailingProperty"/>, computed here, because a trigger cannot test «not empty».
/// </summary>
public static class ButtonIcon
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.RegisterAttached(
        "Kind", typeof(string), typeof(ButtonIcon),
        new FrameworkPropertyMetadata("", OnChanged));

    public static readonly DependencyProperty PlacementProperty = DependencyProperty.RegisterAttached(
        "Placement", typeof(ButtonIconPlacement), typeof(ButtonIcon),
        new FrameworkPropertyMetadata(ButtonIconPlacement.Leading, OnChanged));

    private static readonly DependencyPropertyKey HasLeadingKey = DependencyProperty.RegisterAttachedReadOnly(
        "HasLeading", typeof(bool), typeof(ButtonIcon), new FrameworkPropertyMetadata(false));

    private static readonly DependencyPropertyKey HasTrailingKey = DependencyProperty.RegisterAttachedReadOnly(
        "HasTrailing", typeof(bool), typeof(ButtonIcon), new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty HasLeadingProperty = HasLeadingKey.DependencyProperty;

    public static readonly DependencyProperty HasTrailingProperty = HasTrailingKey.DependencyProperty;

    public static string GetKind(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (string)element.GetValue(KindProperty);
    }

    public static void SetKind(DependencyObject element, string value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(KindProperty, value);
    }

    public static ButtonIconPlacement GetPlacement(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (ButtonIconPlacement)element.GetValue(PlacementProperty);
    }

    public static void SetPlacement(DependencyObject element, ButtonIconPlacement value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(PlacementProperty, value);
    }

    public static bool GetHasLeading(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(HasLeadingProperty);
    }

    public static bool GetHasTrailing(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(HasTrailingProperty);
    }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var hasIcon = !string.IsNullOrEmpty(GetKind(d));
        var trailing = GetPlacement(d) == ButtonIconPlacement.Trailing;
        d.SetValue(HasLeadingKey, hasIcon && !trailing);
        d.SetValue(HasTrailingKey, hasIcon && trailing);
    }
}
