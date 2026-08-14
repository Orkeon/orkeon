using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Orkeon.Studio.Wpf.Views;

/// <summary>Collapses an element while a collection count is zero (guidance strip, validation lists).</summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int count && count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
