using System.Windows;
using System.Windows.Controls;

namespace Orkeon.Studio.Wpf.Controls;

/// <summary>
/// The four heights of a button (refonte R4): 28 for the button of a property row, 32 for
/// toolbars and rows, 40 the default (fields, lists, buttons), 48 for the main input of a
/// screen. Each height carries its own radius (6, 8, 10, 10), padding and font size, so a
/// view says only <c>controls:ButtonSize.Height="32"</c> and the three button styles
/// (<c>BtnPrimary</c>, <c>BtnSecondary</c>, <c>BtnDanger</c>) draw the rest.
/// The template reads <see cref="CornerRadiusProperty"/> rather than a literal radius.
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
            <= 32 => (8.0, 12.0, 13.0),
            <= 40 => (10.0, 16.0, 13.5),
            _ => (10.0, 20.0, 15.0),
        };

        control.Height = (double)e.NewValue;
        control.Padding = new Thickness(padding, 0, padding, 0);
        control.FontSize = fontSize;
        SetCornerRadius(control, new CornerRadius(radius));
    }
}
