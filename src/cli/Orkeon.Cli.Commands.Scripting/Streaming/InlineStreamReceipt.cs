namespace Orkeon.Cli.Commands.Scripting.Streaming;

/// <summary>
/// Whether the answer a flow produced was already shown on the console in the foreground (#228). The dispatch
/// opens one receipt per agent request and per crew run, in its own async flow; every turn the console sink
/// opens in that flow (<see cref="ConsoleLlmDeltaSink.BeginTurn"/>) holds the receipt, and its end records
/// whether its text was written unprefixed on the main console - streamed inline, or as whole unprefixed lines
/// when another turn held the inline claim. A turn written with a ticket or label prefix, or on the thread of
/// a background instance, records nothing shown. <see cref="Streamed"/> tells the script it need not print
/// the payload again.
/// </summary>
/// <remarks>
/// The receipt is monotonic: once a turn of the flow was shown, a later turn of the same flow never resets it,
/// so the outcome does not depend on which of several concurrent turns (a <c>Promise.all</c> of acts, the
/// parallel tasks of a crew) ends last. The ambient is async-flow-local, like
/// <see cref="Progress.ProgressAmbient"/>: two commands running at once each read their own receipt, whatever
/// thread their turns stream on. <see cref="Open"/> is called from an async method, whose flow is restored when
/// it returns, so the receipt never leaks into the caller.
/// </remarks>
internal sealed class InlineStreamReceipt
{
    private static readonly AsyncLocal<InlineStreamReceipt?> _current = new();
    private int _streamed;

    /// <summary>The receipt of the current flow, or null when none was opened.</summary>
    internal static InlineStreamReceipt? Current => _current.Value;

    /// <summary>Opens a fresh receipt for the current flow; nothing has been shown yet.</summary>
    internal static InlineStreamReceipt Open()
    {
        var receipt = new InlineStreamReceipt();
        _current.Value = receipt;
        return receipt;
    }

    /// <summary>Whether any turn of the flow wrote its text unprefixed on the console in the foreground.</summary>
    internal bool Streamed => Volatile.Read(ref _streamed) == 1;

    /// <summary>
    /// Records the end of one turn that wrote text: <paramref name="shown"/> when it was written unprefixed on
    /// the main console. A turn that was not shown leaves the receipt as it was.
    /// </summary>
    internal void Record(bool shown)
    {
        if (shown)
            Volatile.Write(ref _streamed, 1);
    }
}
