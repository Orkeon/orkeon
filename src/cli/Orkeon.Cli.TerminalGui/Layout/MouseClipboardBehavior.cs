using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>
/// Wires application-wide right-click → paste behaviour. A right click
/// anywhere in the TUI grabs the current OS clipboard contents and inserts
/// them at the cursor position of the REPL <c>_input</c> field, which is the
/// only editable surface in the split-pane.
/// </summary>
public static class MouseClipboardBehavior
{
    /// <summary>
    /// Subscribes to <see cref="Application.MouseEvent"/> and, on a right-click,
    /// pastes the OS clipboard into the input returned by <paramref name="getReplInput"/>.
    /// Returns an <see cref="IDisposable"/> to unsubscribe (used by tests; in production
    /// the subscription lives for the lifetime of the TUI).
    /// </summary>
    /// <param name="getReplInput">Lazy accessor for the REPL input field. Called on each click.</param>
    /// <returns>Disposable subscription.</returns>
    public static IDisposable AttachRightClickPaste(Func<TextView?> getReplInput)
    {
        ArgumentNullException.ThrowIfNull(getReplInput);

        EventHandler<Mouse> handler = (_, e) =>
        {
            if (!e.Flags.HasFlag(MouseFlags.RightButtonClicked)) return;

            var input = getReplInput();
            if (input is null) return;

            // Same OS-clipboard paste path as the Ctrl+V keyboard shortcut.
            if (PaneClipboard.PasteInto(input))
                e.Handled = true;
        };

        Application.MouseEvent += handler;
        return new Unsubscriber(() => Application.MouseEvent -= handler);
    }

    private sealed class Unsubscriber : IDisposable
    {
        private Action? _unsubscribe;
        public Unsubscriber(Action unsubscribe) { _unsubscribe = unsubscribe; }
        public void Dispose()
        {
            var u = Interlocked.Exchange(ref _unsubscribe, null);
            u?.Invoke();
        }
    }
}
