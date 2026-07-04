using Terminal.Gui.App;

namespace Orkeon.Cli.TerminalGui.Layout;

internal sealed class TerminalGuiDispatcher : IUiDispatcher
{
    public static readonly TerminalGuiDispatcher Instance = new();
    public void Invoke(Action action) => Application.Invoke(action);
}

internal sealed class InlineDispatcher : IUiDispatcher
{
    public static readonly InlineDispatcher Instance = new();
    private readonly Lock _gate = new();

    // Run inline, but serialize concurrent callers — mirroring TerminalGuiDispatcher, which funnels
    // every action onto Application.Invoke's single UI thread. Without this, parallel callers would
    // race straight into non-thread-safe Terminal.Gui views (e.g. TextView), which is not how the
    // production dispatcher behaves.
    public void Invoke(Action action)
    {
        lock (_gate) action();
    }
}
