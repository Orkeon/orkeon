using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Orkeon.Studio.Wpf.Views;

/// <summary>
/// Shows an element when the bound flag is <see langword="false"/>. The framework ships the positive
/// direction (<c>BooleanToVisibilityConverter</c>) but not this one, and the option panels of spec
/// §5.2 need both: an option that does not apply to the detected shape is hidden, not merely greyed.
/// </summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Collapsed or Visibility.Hidden;
}

/// <summary>
/// Checks a PickRow when its item is the ViewModel's current selection. The catalogue rows
/// live in an ItemsControl (no selector), so each RadioButton compares its own item against
/// the selection property; the write direction goes through a command or Checked handler.
/// </summary>
public sealed class IsEqualConverter : IMultiValueConverter
{
    /// <inheritdoc />
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values is { Length: 2 } && Equals(values[0], values[1]);

    /// <inheritdoc />
    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("This converter is one-way: equality cannot be inverted.");
}

/// <summary>Shows an element when the bound string has content.</summary>
public sealed class StringPresentToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    /// <remarks>
    /// Pass <c>invert</c> as the parameter for the other direction — a placeholder that
    /// shows only while the field is empty is the same question read backwards.
    /// </remarks>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var present = value is string { Length: > 0 };
        if (string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase))
            present = !present;

        return present ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("This converter is one-way: a Visibility carries no string.");
}

/// <summary>0/1/2 → the design's verbosity vocabulary (normal / detailed / diagnostic).</summary>
/// <summary>
/// The mock's modal body height — <c>min(560px, 62vh)</c> (T-13): fed the window's
/// ActualHeight, hands back the bound the body's ScrollViewer may grow to.
/// </summary>
/// <summary>A pixel count into a left-only margin — the folder tree's per-level indent.</summary>
public sealed class LeftIndentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        new Thickness(value is double left ? left : 0, 0, 0, 0);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class ModalBodyHeightConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double height && height > 0 ? Math.Min(560d, height * 0.62) : 560d;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class VerbosityLabelConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        value is int level
            ? Services.I18n.Instance[level switch
            {
                0 => "Run_Verb_Normal",
                1 => "Run_Verb_Detailed",
                _ => "Run_Verb_Diagnostic",
            }]
            : "";

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Every bound value true → Visible, anything else → Collapsed. The draft note is the case
/// it exists for: it shows when a draft stands AND the wizard is not the screen in front,
/// two conditions that live on different objects.
/// </summary>
public sealed class AllTrueToVisibilityConverter : IMultiValueConverter
{
    /// <inheritdoc />
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values is not null && values.Length > 0 && values.All(v => v is true)
            ? Visibility.Visible
            : Visibility.Collapsed;

    /// <inheritdoc />
    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Boolean inverter — a null (an unset IsChecked) reads as false, so it negates to true.</summary>
public sealed class NegateConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;
}

/// <summary>
/// Something present → Visible, null → Collapsed. Used for panes that only make sense once
/// a selection exists: a form standing open over nothing reads as a screen in error.
/// </summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("This converter is one-way: a Visibility carries no instance.");
}
