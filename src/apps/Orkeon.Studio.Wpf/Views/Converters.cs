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

/// <summary>Shows an element when the bound string has content.</summary>
public sealed class StringPresentToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string { Length: > 0 } ? Visibility.Visible : Visibility.Collapsed;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("This converter is one-way: a Visibility carries no string.");
}
