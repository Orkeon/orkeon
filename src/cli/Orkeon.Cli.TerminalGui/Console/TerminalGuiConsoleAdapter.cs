using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.TerminalGui.Layout;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;

namespace Orkeon.Cli.TerminalGui.Console;

/// <summary>
/// Marshals all <see cref="IConsoleAdapter"/> operations to a <see cref="ReplPaneView"/>.
/// </summary>
/// <remarks>
/// <para>
///   <see cref="ReadLine"/> is sync-over-async — safe because the runner's
///   <c>RunAsync</c> executes on a background <see cref="Task"/>, never on the
///   Terminal.Gui main loop thread. Callers that can await should prefer
///   <see cref="ReadLineAsync"/> / <see cref="ReadKeyAsync"/>, which override the
///   sync-delegating <see cref="IConsoleAdapter"/> defaults with the pane's real
///   asynchronous input pipeline (no pinned thread).
/// </para>
/// <para>
///   <see cref="Write"/> uses a heuristic to detect prompt prefixes
///   (<see cref="LooksLikePrompt"/>): single-line text ending in <c>"&gt; "</c> or <c>"&gt;"</c>
///   is routed to the prompt label instead of the history. This keeps the
///   existing <see cref="Orkeon.Cli.Abstractions.Runners.InteractiveRunnerBase"/>
///   contract unchanged.
/// </para>
/// </remarks>
public sealed class TerminalGuiConsoleAdapter : IConsoleAdapter
{
    private readonly ReplPaneView _repl;

    public TerminalGuiConsoleAdapter(ReplPaneView repl)
    {
        _repl = repl ?? throw new ArgumentNullException(nameof(repl));
    }

    public void Write(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (LooksLikePrompt(text))
        {
            _repl.SetPromptPrefix(text);
            return;
        }
        _repl.AppendOutput(text);
    }

    public void WriteLine(string text) => _repl.AppendOutputLine(text);

    // Assumed-blocking by design — audited (ANT-007/ANT-010, round-02); see docs/architecture/scripting.md.
    public string? ReadLine()
    {
        try
        {
            return _repl.ReadLineAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// True-async line read backed by <see cref="ReplPaneView.ReadLineAsync"/> — overrides the
    /// sync-delegating <see cref="IConsoleAdapter"/> default so awaiting callers do not pin a
    /// thread. Returns <c>null</c> when the read is cancelled (token or
    /// <see cref="ReplPaneView.CancelPendingRead"/>), mirroring <see cref="ReadLine"/>.
    /// </summary>
    public async Task<string?> ReadLineAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _repl.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    // Assumed-blocking by design — audited (ANT-007/ANT-010, round-02); see docs/architecture/scripting.md.
    public ConsoleKeyInfo ReadKey(bool intercept = false)
        => ReadKeyAsync(intercept, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>
    /// True-async key read backed by the pane's <see cref="ReplPaneView.KeyCaptured"/> event —
    /// overrides the sync-delegating <see cref="IConsoleAdapter"/> default. Cancelling the token
    /// detaches the listener and cancels the returned task.
    /// </summary>
    public async Task<ConsoleKeyInfo> ReadKeyAsync(bool intercept = false, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<ConsoleKeyInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<KeyCapturedEventArgs>? handler = null;
        handler = (_, e) =>
        {
            if (intercept) e.Key.Handled = true;
            _repl.KeyCaptured -= handler!;
            tcs.TrySetResult(ConsoleKeyMapping.GetConsoleKeyInfoFromKeyCode(e.Key.KeyCode));
        };
        _repl.KeyCaptured += handler;
        using var registration = cancellationToken.Register(() =>
        {
            _repl.KeyCaptured -= handler!;
            tcs.TrySetCanceled(cancellationToken);
        });
        return await tcs.Task.ConfigureAwait(false);
    }

    public void Clear() => _repl.Clear();

    internal static bool LooksLikePrompt(string text)
        => text.Length > 0 && !text.Contains('\n', StringComparison.Ordinal) && (text.EndsWith("> ", StringComparison.Ordinal) || text.EndsWith('>'));
}
