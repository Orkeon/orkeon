using Orkeon.Application.Interfaces.Ports;
using Orkeon.Cli.Abstractions.Console;

namespace Orkeon.Cli.Scripting.Streaming;

/// <summary>
/// Renders streamed <c>ctx.llm.act</c> content deltas incrementally on the host console
/// (exp 07 F5 L3 — native rendering, no script-side <c>onDelta</c> required). Deltas are
/// written verbatim without a newline; the turn terminator closes the line so subsequent
/// REPL output starts clean.
/// </summary>
/// <remarks>
/// Called from the streaming enumeration's pool thread — same threading profile as the
/// async command completions that already write through <see cref="IConsoleAdapter"/>
/// (the Terminal.Gui adapter marshals internally, the plain adapters append-only).
/// </remarks>
public sealed class ConsoleLlmDeltaSink : ILlmDeltaSink
{
    private readonly IConsoleAdapter _console;

    /// <summary>Creates the sink over the host console adapter.</summary>
    public ConsoleLlmDeltaSink(IConsoleAdapter console)
        => _console = console ?? throw new ArgumentNullException(nameof(console));

    /// <inheritdoc />
    public void OnDelta(string delta) => _console.Write(delta);

    /// <inheritdoc />
    public void OnTurnCompleted() => _console.WriteLine(string.Empty);
}
