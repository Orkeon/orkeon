namespace Orkeon.Cli.Commands.Scripting.Streaming;

/// <summary>
/// Whether the answer a flow produced already streamed inline to the console (#228). The dispatch opens one
/// receipt per agent request and per crew run, in its own async flow; every turn the console sink opens in
/// that flow (<see cref="ConsoleLlmDeltaSink.BeginTurn"/>) holds the receipt, and its end records whether it
/// was the inline one. The last turn of the flow is the one that produced the payload, so
/// <see cref="Streamed"/> tells the script it need not print the payload again.
/// </summary>
/// <remarks>
/// The ambient is async-flow-local, like <see cref="Progress.ProgressAmbient"/>: two commands running at once
/// each read their own receipt, whatever thread their turns stream on. <see cref="Open"/> is called from an
/// async method, whose flow is restored when it returns, so the receipt never leaks into the caller.
/// </remarks>
internal sealed class InlineStreamReceipt
{
    private static readonly AsyncLocal<InlineStreamReceipt?> _current = new();
    private int _streamed;

    /// <summary>The receipt of the current flow, or null when none was opened.</summary>
    internal static InlineStreamReceipt? Current => _current.Value;

    /// <summary>Opens a fresh receipt for the current flow; nothing has streamed yet.</summary>
    internal static InlineStreamReceipt Open()
    {
        var receipt = new InlineStreamReceipt();
        _current.Value = receipt;
        return receipt;
    }

    /// <summary>Whether the last turn that streamed text in the flow streamed inline to the console.</summary>
    internal bool Streamed => Volatile.Read(ref _streamed) == 1;

    /// <summary>Records the end of one turn that streamed text: inline, or as whole prefixed lines.</summary>
    internal void Record(bool inline) => Volatile.Write(ref _streamed, inline ? 1 : 0);
}
