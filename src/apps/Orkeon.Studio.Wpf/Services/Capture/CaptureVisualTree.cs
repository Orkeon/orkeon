using System.Windows;
using System.Windows.Media;

namespace Orkeon.Studio.Wpf.Services.Capture;

/// <summary>
/// Walking the visual tree, in one place. The guided tour needed it to find a spotlight anchor and
/// the capture campaign needs it to find spinners, popups and panels; a second copy of the same
/// recursion is how the two would drift.
/// </summary>
internal static class CaptureVisualTree
{
    /// <summary>
    /// Every visual descendant, depth first. A <see cref="System.Windows.Controls.Primitives.Popup"/>
    /// is yielded like any other child — but its <c>Child</c> is NOT, because a popup hosts its
    /// content in a separate visual tree of its own. That is exactly why the campaign has to
    /// composite popups rather than trusting one render of the window.
    /// </summary>
    public static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;

            foreach (var nested in Descendants(child))
                yield return nested;
        }
    }

    /// <summary>
    /// Finds a named element anywhere under <paramref name="root"/>.
    /// <para>
    /// <see cref="FrameworkElement.FindName(string)"/> alone cannot see names registered in a
    /// UserControl's own namescope, so the walk is what keeps a lookup working whatever view
    /// declares the anchor.
    /// </para>
    /// </summary>
    public static FrameworkElement? FindByName(DependencyObject root, string name) =>
        Descendants(root).OfType<FrameworkElement>()
            .FirstOrDefault(element => string.Equals(element.Name, name, StringComparison.Ordinal));

    /// <summary>The topmost visual ancestor of <paramref name="visual"/> — a popup's own root.</summary>
    public static DependencyObject TopmostParent(DependencyObject visual)
    {
        ArgumentNullException.ThrowIfNull(visual);

        var current = visual;
        while (VisualTreeHelper.GetParent(current) is { } parent)
            current = parent;

        return current;
    }
}
