using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Orkeon.Studio.Run.Tests.Views;

/// <summary>
/// Reads and drives a view built without a terminal: finds its subviews by type or caption
/// and presses a button the way the keyboard would (the Accept command raises Accepting).
/// </summary>
internal static class ViewTree
{
    /// <summary>Every view of type <typeparamref name="T"/> under <paramref name="root"/>, depth first.</summary>
    public static List<T> All<T>(View root) where T : View
    {
        var found = new List<T>();
        Walk(root, found);
        return found;
    }

    /// <summary>The single view of type <typeparamref name="T"/> under <paramref name="root"/>.</summary>
    public static T One<T>(View root) where T : View => Assert.Single(All<T>(root));

    /// <summary>The button whose caption is <paramref name="text"/>.</summary>
    public static Button Button(View root, string text)
        => Assert.Single(All<Button>(root), b => b.Text == text);

    /// <summary>Presses <paramref name="button"/>.</summary>
    public static void Press(Button button) => button.InvokeCommand(Command.Accept);

    /// <summary>The rows a list view shows.</summary>
    public static List<string> Rows(ListView list)
        => list.Source is null ? [] : list.Source.ToList().Cast<object>().Select(o => o?.ToString() ?? string.Empty).ToList();

    private static void Walk<T>(View view, List<T> found) where T : View
    {
        foreach (var child in view.SubViews)
        {
            if (child is T match)
                found.Add(match);
            Walk(child, found);
        }
    }
}
