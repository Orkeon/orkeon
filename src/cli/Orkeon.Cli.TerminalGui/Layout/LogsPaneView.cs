using Microsoft.Extensions.Logging;
using Orkeon.Cli.TerminalGui.Hosting;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>
/// Read-only scrollable pane for log output. Backed by an in-memory ring buffer
/// so memory stays bounded even under heavy log volume.
/// </summary>
/// <remarks>
/// Issue #196: the ring buffer is fed on the caller's thread and costs no UI work while the pane is
/// hidden. While it is visible, entries are coalesced (<see cref="AppendCoalescer{T}"/>, one UI
/// dispatch per burst) and appended to a line-based <see cref="TranscriptView"/> that wraps only the
/// new lines and drops head lines past the capacity. Showing the pane renders the buffer once in full.
/// </remarks>
public sealed class LogsPaneView : FrameView
{
    // A posted entry: its sequence number and its text (null for a clear).
    private readonly record struct LogOp(long Seq, string? Line);

    private readonly LinkedList<string> _buffer = new();
    private readonly int _capacity;
    private readonly string _baseTitle;
    private readonly TranscriptView _textView;
    private readonly IUiDispatcher _dispatcher;
    private readonly AppendCoalescer<LogOp> _coalescer;
    private readonly Lock _gate = new();
    private long _seq;
    private bool _rendering = true;

    // UI thread only: the last sequence number the view reflects; ops at or below it are already shown.
    private long _renderedSeq;

    public LogsPaneView(TerminalGuiOptions options)
        : this(options, TerminalGuiDispatcher.Instance) { }

    internal LogsPaneView(TerminalGuiOptions options, IUiDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(options);
        _capacity = options.LogsBufferCapacity;
        _dispatcher = dispatcher;
        _coalescer = new AppendCoalescer<LogOp>(dispatcher, ApplyBatch);
        _baseTitle = options.LogsPaneTitle;
        Title = _baseTitle;
        SetScheme(SchemeFactory.Pane());

        _textView = new TranscriptView(_capacity)
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            WordWrap = true,
            CanFocus = true,
        };
        _textView.SetScheme(SchemeFactory.Pane());
        Add(_textView);
    }

    public int LineCount
    {
        get { lock (_gate) return _buffer.Count; }
    }

    public void Append(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        lock (_gate)
        {
            _buffer.AddLast(line);
            while (_buffer.Count > _capacity)
                _buffer.RemoveFirst();
            var seq = ++_seq;
            // Posted under the gate so the coalescer keeps the buffer's order. The coalescer calls the
            // dispatcher outside its own lock and never takes this gate.
            if (_rendering) _coalescer.Post(new LogOp(seq, line));
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _buffer.Clear();
            var seq = ++_seq;
            if (_rendering) _coalescer.Post(new LogOp(seq, null));
        }
    }

    /// <summary>
    /// Updates the frame title to advertise the active visible log level.
    /// Called by the global key bindings after every F2 / Shift+F2 keystroke.
    /// Title format: <c>"Logs — Level: Information"</c>.
    /// </summary>
    public void SetVisibleLevel(LogLevel level)
    {
        _dispatcher.Invoke(() => Title = $"{_baseTitle} — Level: {level}");
    }

    /// <summary>Test-only accessor for the rendered transcript text.</summary>
    internal string CurrentText => _textView.TranscriptText;

    /// <summary>Test-only accessor for the rendered model (it counts the wrap work).</summary>
    internal TranscriptBuffer RenderedModel => _textView.Buffer;

    /// <inheritdoc />
    protected override void OnVisibleChanged()
    {
        base.OnVisibleChanged();
        if (!Visible)
        {
            lock (_gate) _rendering = false;
            return;
        }

        // Shown: render the whole buffer once; the ops posted up to this point are skipped.
        string[] snapshot;
        lock (_gate)
        {
            snapshot = [.. _buffer];
            _renderedSeq = _seq;
            _rendering = true;
        }
        _textView.Clear();
        _textView.AppendLines(snapshot);
        _textView.MoveEnd();
    }

    private void ApplyBatch(IReadOnlyList<LogOp> batch)
    {
        // Only the ops after the last clear matter, and of those at most the capacity's worth of lines.
        var from = 0;
        var cleared = false;
        for (var i = batch.Count - 1; i >= 0 && batch[i].Seq > _renderedSeq; i--)
        {
            if (batch[i].Line is not null) continue;
            from = i + 1;
            cleared = true;
            break;
        }
        var lines = new List<string>();
        for (var i = from; i < batch.Count; i++)
        {
            if (batch[i].Seq > _renderedSeq && batch[i].Line is { } line) lines.Add(line);
        }
        _renderedSeq = Math.Max(_renderedSeq, batch[^1].Seq);
        if (cleared) _textView.Clear();
        if (lines.Count > _capacity) lines.RemoveRange(0, lines.Count - _capacity);
        if (lines.Count == 0) return;
        _textView.AppendLines(lines);
        _textView.MoveEnd();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Terminal.Gui's base View.Dispose also disposes the Add()-ed _textView; its
            // IsDisposed guard makes this explicit call an idempotent no-op.
            _textView.Dispose();
        }
        base.Dispose(disposing);
    }
}
