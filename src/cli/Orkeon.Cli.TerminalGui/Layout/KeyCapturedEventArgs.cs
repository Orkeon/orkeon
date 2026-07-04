using Terminal.Gui.Input;

namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>
/// Event data for <see cref="ReplPaneView.KeyCaptured"/>, carrying the captured key.
/// Setting <see cref="Terminal.Gui.Input.Key.Handled"/> on <see cref="Key"/> suppresses
/// the pane's default handling of that key.
/// </summary>
public sealed class KeyCapturedEventArgs : EventArgs
{
    /// <summary>Initializes a new instance carrying the captured <paramref name="key"/>.</summary>
    public KeyCapturedEventArgs(Key key) => Key = key;

    /// <summary>The key that was captured on the input field.</summary>
    public Key Key { get; }
}
