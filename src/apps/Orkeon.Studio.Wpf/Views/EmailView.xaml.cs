using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace Orkeon.Studio.Wpf.Views;

/// <summary>The E-mail settings tab (STUDIO-67). XAML wiring only; every behaviour lives in the ViewModels.</summary>
public partial class EmailView : UserControl
{
    /// <summary>Loads the XAML; picking another account brings its form on screen (R24).</summary>
    public EmailView()
    {
        InitializeComponent();
        SelectionReveal.Attach(AccountList, AccountForm);
    }
}

/// <summary>
/// R24 (D11): the form of the account picked in a list sits under the list, often below the
/// fold, so picking another account used to change nothing on screen. On a change of selection
/// the form is scrolled into view - smoothly when the user picked it in the list, at once when
/// the selection was set by code (a capture stop, a restore), so a photograph never catches it
/// halfway. Only the scroll position moves: no view model is touched. The first selection, made
/// when the tab is built, does not scroll.
/// </summary>
internal static class SelectionReveal
{
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(240);

    /// <summary>Scrolls <paramref name="editor"/> into view whenever <paramref name="list"/> moves to another item.</summary>
    public static void Attach(Selector list, FrameworkElement editor)
    {
        list.SelectionChanged += (_, e) =>
        {
            if (!ReferenceEquals(e.OriginalSource, list) || e.AddedItems.Count == 0 || e.RemovedItems.Count == 0)
                return;

            var smooth = list.IsKeyboardFocusWithin;
            // After layout: the form has just been rebuilt over the new account.
            editor.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => Reveal(editor, smooth));
        };
    }

    private static void Reveal(FrameworkElement editor, bool smooth)
    {
        if (!editor.IsVisible || FindViewer(editor) is not { } viewer)
            return;

        var top = editor.TransformToAncestor(viewer).Transform(new Point(0, 0)).Y;
        var bottom = top + editor.ActualHeight;
        var viewport = viewer.ViewportHeight;

        // The least move that shows it; a form taller than the viewport shows from its top.
        double delta;
        if (top < 0)
            delta = top;
        else if (bottom > viewport)
            delta = Math.Min(top, bottom - viewport);
        else
            return;

        var target = Math.Clamp(viewer.VerticalOffset + delta, 0, viewer.ScrollableHeight);
        if (smooth && SystemParameters.ClientAreaAnimation)
            Glide(viewer, target);
        else
            viewer.ScrollToVerticalOffset(target);
    }

    /// <summary>An ease-out glide to <paramref name="target"/>, one step per rendered frame.</summary>
    private static void Glide(ScrollViewer viewer, double target)
    {
        var from = viewer.VerticalOffset;
        var clock = Stopwatch.StartNew();
        EventHandler? step = null;
        step = (_, _) =>
        {
            var t = Math.Min(1, clock.Elapsed.TotalMilliseconds / Duration.TotalMilliseconds);
            viewer.ScrollToVerticalOffset(from + ((target - from) * (1 - Math.Pow(1 - t, 3))));
            if (t >= 1)
                CompositionTarget.Rendering -= step;
        };
        CompositionTarget.Rendering += step;
    }

    private static ScrollViewer? FindViewer(DependencyObject start)
    {
        for (var node = VisualTreeHelper.GetParent(start); node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is ScrollViewer viewer)
                return viewer;
        }

        return null;
    }
}
