using Terminal.Gui.ViewBase;

namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>Accept handlers that keep an Accept to the view it was raised on.</summary>
public static class AcceptHandlers
{
    /// <summary>
    /// Runs <paramref name="action"/> when <paramref name="view"/> is accepted, and marks the Accept
    /// handled: an Accept left unhandled travels on to the <c>IsDefault</c> button of the same window,
    /// whose action would then run too (a Cancel that also confirms, a Validate that also runs).
    /// </summary>
    public static void OnAccept(this View view, Action action)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(action);
        view.Accepting += (_, e) =>
        {
            action();
            e.Handled = true;
        };
    }
}
