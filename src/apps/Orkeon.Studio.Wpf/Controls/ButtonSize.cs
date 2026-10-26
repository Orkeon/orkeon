using System.Windows;
using System.Windows.Controls;

namespace Orkeon.Studio.Wpf.Controls;

/// <summary>
/// The heights of a button. hrm R05: 40 is the default (a screen's actions, a form row), 32 the
/// compact one (cards, lists, compact bars, the title bar); 28 survives for the button of a
/// property row and 48 for the main input of a screen. Radius 10 from 32 up, 6 at 28 (the
/// small square ones). Each height carries its padding and font size, so a view says only
/// <c>controls:ButtonSize.Height="32"</c> and every style based on <c>BtnBase</c> draws the
/// rest. The template reads <see cref="CornerRadiusProperty"/> rather than a literal radius.
/// </summary>
public static class ButtonSize
{
    public static readonly DependencyProperty HeightProperty = DependencyProperty.RegisterAttached(
        "Height", typeof(double), typeof(ButtonSize),
        new FrameworkPropertyMetadata(0.0, OnHeightChanged));

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.RegisterAttached(
        "CornerRadius", typeof(CornerRadius), typeof(ButtonSize),
        new FrameworkPropertyMetadata(new CornerRadius(10), FrameworkPropertyMetadataOptions.AffectsRender));

    public static double GetHeight(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (double)element.GetValue(HeightProperty);
    }

    public static void SetHeight(DependencyObject element, double value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(HeightProperty, value);
    }

    public static CornerRadius GetCornerRadius(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (CornerRadius)element.GetValue(CornerRadiusProperty);
    }

    public static void SetCornerRadius(DependencyObject element, CornerRadius value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(CornerRadiusProperty, value);
    }

    private static void OnHeightChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Control control)
        {
            return;
        }

        var (radius, padding, fontSize) = (double)e.NewValue switch
        {
            <= 28 => (6.0, 10.0, 13.0),
            <= 32 => (10.0, 12.0, 13.0),
            <= 40 => (10.0, 16.0, 14.0),
            _ => (10.0, 20.0, 15.0),
        };

        control.Height = (double)e.NewValue;
        control.Padding = new Thickness(padding, 0, padding, 0);
        control.FontSize = fontSize;
        SetCornerRadius(control, new CornerRadius(radius));
    }
}
