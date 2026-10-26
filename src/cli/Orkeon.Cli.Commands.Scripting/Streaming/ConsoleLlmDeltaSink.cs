using System.Text;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Commands.Scripting.Progress;

namespace Orkeon.Cli.Commands.Scripting.Streaming;

/// <summary>
/// Renders streamed <c>ctx.llm.act</c> content deltas on the host console (native rendering, no
/// script-side <c>onDelta</c> required). The foreground act - the first turn without a prefix to
/// write - streams straight through, token by token, as before. Every other turn (a second act
/// streaming at once, an act under a background command instance, a crew task) keeps its own line
/// buffer and writes one whole line at a time, prefixed with the instance's ticket (<c>[t2] </c>),
/// as the completion lines are; a line of theirs that arrives while the foreground act is inside
/// a line waits for that line's end. So two acts streaming at once never write into one another
/// (#211) and no line of either is ever cut.
/// </summary>
/// <remarks>
/// Called from the streaming enumeration's pool thread — same threading profile as the
/// async command completions that already write through <see cref="IConsoleAdapter"/>
/// (the Terminal.Gui adapter marshals internally, the plain adapters append-only). Every write
/// goes through one lock, so the lines stay whole on the plain adapters too.
/// </remarks>
public sealed class ConsoleLlmDeltaSink : ILlmDeltaSink
{
    private readonly IConsoleAdapter _console;
    private readonly object _lock = new();
    private readonly Queue<string> _held = new();
    private TurnSink? _inline;
    private bool _inlineMidLine;
    private TurnSink? _defaultTurn;

    /// <summary>Creates the sink over the host console adapter.</summary>
    public ConsoleLlmDeltaSink(IConsoleAdapter console)
        => _console = console ?? throw new ArgumentNullException(nameof(console));

    /// <summary>
    /// Opens one turn. Its lines carry <c>[&lt;ticket&gt; &lt;label&gt;] </c> when the turn runs under a
    /// command instance (<see cref="ProgressAmbient.CurrentInstance"/>: a background command, a
    /// <c>runAgent</c> sub-run) or the caller labelled it (a crew's task), and are then written whole;
    /// an unlabelled turn streams straight through when no other does, else writes whole lines too.
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
    public void OnDelta(string delta)
    {
        TurnSink turn;
        lock (_lock)
            turn = _defaultTurn ??= new TurnSink(this, string.Empty);
        turn.OnDelta(delta);
    }

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

    /// <summary>Whether <paramref name="turn"/> may stream straight through: no other turn does.</summary>
    private bool TryClaimInline(TurnSink turn)
    {
        lock (_lock)
        {
            if (_inline is not null && _inline != turn)
                return false;
            _inline = turn;
            return true;
        }
    }

    /// <summary>A delta of the inline turn: written as it is; a complete line releases the held lines.</summary>
    private void WriteInline(string delta)
    {
        lock (_lock)
        {
            _console.Write(delta);
            _inlineMidLine = !delta.EndsWith('\n');
            if (!_inlineMidLine)
                Release();
        }
    }

    /// <summary>The end of the inline turn: the line terminator the buffered rendering wrote, then the held lines.</summary>
    private void EndInline(TurnSink turn)
    {
        lock (_lock)
        {
            if (_inline != turn)
                return;
            _console.WriteLine(string.Empty);
            _inline = null;
            _inlineMidLine = false;
            Release();
        }
    }

    /// <summary>A whole line of a buffered turn: held while the inline turn is inside a line, written otherwise.</summary>
    private void WriteLine(string line)
    {
        lock (_lock)
        {
            if (_inline is not null && _inlineMidLine)
                _held.Enqueue(line);
            else
                _console.WriteLine(line);
        }
    }

    private void Release()
    {
        while (_held.TryDequeue(out var line))
            _console.WriteLine(line);
    }

    /// <summary>One turn: inline when it is the only unprefixed one, else a line buffer flushed line by line.</summary>
    private sealed class TurnSink(ConsoleLlmDeltaSink owner, string prefix) : ILlmDeltaSink
    {
        private readonly StringBuilder _line = new();
        private readonly object _gate = new();
        private bool? _inline;
        private int _scanFrom;

        public void OnDelta(string delta)
        {
            lock (_gate)
            {
                _inline ??= prefix.Length == 0 && owner.TryClaimInline(this);
                if (_inline.Value)
                {
                    owner.WriteInline(delta);
                    return;
                }

                _line.Append(delta);
                FlushCompleteLines();
            }
        }

        /// <summary>
        /// The inline turn writes the line terminator the buffered rendering wrote; a buffered one writes
        /// what the last newline left, an unprefixed one its terminator too, and a prefixed one with
        /// nothing left writes no bare prefix.
        /// </summary>
        public void OnTurnCompleted()
        {
            lock (_gate)
            {
                if (_inline == true)
                {
                    owner.EndInline(this);
                    return;
                }

                if (_line.Length > 0 || prefix.Length == 0)
                    owner.WriteLine(prefix + _line);
                _line.Clear();
                _scanFrom = 0;
            }
        }

        public ILlmDeltaSink BeginTurn(string? label) => this;

        /// <summary>Writes every complete line of the buffer; the scan resumes where the last one stopped.</summary>
        private void FlushCompleteLines()
        {
            for (var i = _scanFrom; i < _line.Length; i++)
            {
                if (_line[i] != '\n')
                    continue;
                var end = i > 0 && _line[i - 1] == '\r' ? i - 1 : i;
                owner.WriteLine(prefix + _line.ToString(0, end));
                _line.Remove(0, i + 1);
                i = -1;
            }

            _scanFrom = _line.Length;
        }
    }
}
