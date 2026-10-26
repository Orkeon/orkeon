using System.Text;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Commands.Scripting.Progress;

namespace Orkeon.Cli.Commands.Scripting.Streaming;

/// <summary>
/// Renders streamed <c>ctx.llm.act</c> content deltas on the host console (native rendering, no
/// script-side <c>onDelta</c> required), one whole line at a time: each turn keeps its own line
/// buffer and writes a line once it is complete, the turn terminator writes what is left. Two
/// turns streaming at once (two acts of concurrent commands, the tasks of a parallel wave) thus
/// never write into one another (#211), and a turn that runs under a command instance prefixes
/// its lines with the instance's ticket (<c>[t2] </c>), as the completion lines are, so the
/// reader knows which act each line belongs to.
/// </summary>
/// <remarks>
/// Called from the streaming enumeration's pool thread — same threading profile as the
/// async command completions that already write through <see cref="IConsoleAdapter"/>
/// (the Terminal.Gui adapter marshals internally, the plain adapters append-only). A line is
/// written under one lock, so lines of different turns stay whole on the plain adapters too.
/// </remarks>
public sealed class ConsoleLlmDeltaSink : ILlmDeltaSink
{
    private readonly IConsoleAdapter _console;
    private readonly object _lock = new();
    private TurnSink? _defaultTurn;

    /// <summary>Creates the sink over the host console adapter.</summary>
    public ConsoleLlmDeltaSink(IConsoleAdapter console)
        => _console = console ?? throw new ArgumentNullException(nameof(console));

    /// <summary>
    /// Opens one turn with its own line buffer. Its lines carry <c>[&lt;ticket&gt; &lt;label&gt;] </c>
    /// when the turn runs under a command instance (<see cref="ProgressAmbient.CurrentInstance"/>:
    /// a background command, a <c>runAgent</c> sub-run) or the caller labelled it (a crew's task);
    /// a turn of the foreground command, unlabelled, writes bare lines as before.
    /// </summary>
    /// <param name="label">What the turn belongs to as the caller knows it; null when it knows nothing.</param>
    public ILlmDeltaSink BeginTurn(string? label)
    {
        var parts = new[] { ProgressAmbient.CurrentInstance?.Ticket, label }
            .Where(static p => !string.IsNullOrWhiteSpace(p));
        var tag = string.Join(' ', parts);
        return new TurnSink(this, tag.Length == 0 ? string.Empty : $"[{tag}] ");
    }

    /// <summary>A delta of a caller that opened no turn: it goes to one shared, unlabelled turn.</summary>
    public void OnDelta(string delta) => DefaultTurn().OnDelta(delta);

    /// <summary>The end of the shared turn of the callers that opened none.</summary>
    public void OnTurnCompleted()
    {
        TurnSink? turn;
        lock (_lock)
        {
            turn = _defaultTurn;
            _defaultTurn = null;
        }
        (turn ?? new TurnSink(this, string.Empty)).OnTurnCompleted();
    }

    private TurnSink DefaultTurn()
    {
        lock (_lock)
            return _defaultTurn ??= new TurnSink(this, string.Empty);
    }

    private void WriteLine(string prefix, string line)
    {
        lock (_lock)
            _console.WriteLine(prefix + line);
    }

    /// <summary>One turn: its line buffer, flushed line by line, the rest at its end.</summary>
    private sealed class TurnSink(ConsoleLlmDeltaSink owner, string prefix) : ILlmDeltaSink
    {
        private readonly StringBuilder _line = new();
        private readonly object _gate = new();

        public void OnDelta(string delta)
        {
            lock (_gate)
            {
                _line.Append(delta);
                int newline;
                while ((newline = IndexOfNewline()) >= 0)
                {
                    var end = newline > 0 && _line[newline - 1] == '\r' ? newline - 1 : newline;
                    owner.WriteLine(prefix, _line.ToString(0, end));
                    _line.Remove(0, newline + 1);
                }
            }
        }

        /// <summary>
        /// Writes what the last newline left, then the line terminator the buffered rendering wrote: a
        /// bare turn ends with an empty line as before; a labelled one, with nothing left, writes no
        /// bare label.
        /// </summary>
        public void OnTurnCompleted()
        {
            lock (_gate)
            {
                if (_line.Length > 0 || prefix.Length == 0)
                    owner.WriteLine(prefix, _line.ToString());
                _line.Clear();
            }
        }

        public ILlmDeltaSink BeginTurn(string? label) => this;

        private int IndexOfNewline()
        {
            for (var i = 0; i < _line.Length; i++)
            {
                if (_line[i] == '\n')
                    return i;
            }

            return -1;
        }
    }
}
