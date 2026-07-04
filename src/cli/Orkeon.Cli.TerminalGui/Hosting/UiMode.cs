namespace Orkeon.Cli.TerminalGui.Hosting;

/// <summary>
/// Console UI mode for Orkeon interactive runners.
/// </summary>
public enum UiMode
{
    /// <summary>Plain stdout/stdin (current SystemConsoleAdapter behavior).</summary>
    Plain,
    /// <summary>Terminal.Gui split-pane (logs + REPL).</summary>
    Tui
}
