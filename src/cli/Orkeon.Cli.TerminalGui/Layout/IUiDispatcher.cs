namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>
/// Marshals work onto the Terminal.Gui main loop.
/// Production uses <see cref="TerminalGuiDispatcher"/> (delegates to <c>Application.Invoke</c>);
/// tests use <see cref="InlineDispatcher"/> (synchronous, no <c>Application.Init</c> required).
/// </summary>
internal interface IUiDispatcher
{
    void Invoke(Action action);
}
