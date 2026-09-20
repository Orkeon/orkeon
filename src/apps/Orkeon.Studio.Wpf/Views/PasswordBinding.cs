using System.Windows;
using System.Windows.Controls;

namespace Orkeon.Studio.Wpf.Views;

/// <summary>
/// Binds a <see cref="PasswordBox"/> to a view-model string (T-31). WPF keeps
/// <c>PasswordBox.Password</c> off the dependency-property system on purpose, so the key
/// fields could only be plain text boxes until now. The attached property carries the text
/// both ways: every keystroke reaches the view model, and a view model that wipes the field
/// once the key is stored sees the box wiped too. The default is null rather than empty so
/// that a binding evaluating to "" still counts as a change and hooks the box.
/// </summary>
public static class PasswordBinding
{
    /// <summary>The bound text; two-way by default, like the box it stands in for.</summary>
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text",
        typeof(string),
        typeof(PasswordBinding),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnTextChanged));

    // Set while the box itself is the source of a write, so the echo is not pushed back into it.
    private static readonly DependencyProperty IsPushingProperty = DependencyProperty.RegisterAttached(
        "IsPushing", typeof(bool), typeof(PasswordBinding), new PropertyMetadata(false));

    public static string? GetText(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (string?)element.GetValue(TextProperty);
    }

    public static void SetText(DependencyObject element, string? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(TextProperty, value);
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box)
            return;

        // Subscribed once whatever the number of writes: removing first makes the add idempotent.
        box.PasswordChanged -= OnPasswordChanged;
        box.PasswordChanged += OnPasswordChanged;

        if ((bool)box.GetValue(IsPushingProperty))
            return;

        var text = e.NewValue as string ?? "";
        if (!string.Equals(box.Password, text, StringComparison.Ordinal))
            box.Password = text;
    }

    private static void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox box)
            return;

        box.SetValue(IsPushingProperty, true);
        try
        {
            SetText(box, box.Password);
        }
        finally
        {
            box.SetValue(IsPushingProperty, false);
        }
    }
}
