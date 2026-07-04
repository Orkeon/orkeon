using Microsoft.Extensions.Logging;
using Orkeon.Cli.TerminalGui.Hosting;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>
/// Read-only scrollable pane for log output. Backed by an in-memory ring buffer
/// so memory stays bounded even under heavy log volume.
/// </summary>
public sealed class LogsPaneView : FrameView
{
    private readonly LinkedList<string> _buffer = new();
    private readonly int _capacity;
    private readonly string _baseTitle;
    private readonly TextView _textView;
    private readonly IUiDispatcher _dispatcher;
    private readonly Lock _gate = new();

    public LogsPaneView(TerminalGuiOptions options)
        : this(options, TerminalGuiDispatcher.Instance) { }

    internal LogsPaneView(TerminalGuiOptions options, IUiDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(options);
        _capacity = options.LogsBufferCapacity;
        _dispatcher = dispatcher;
        _baseTitle = options.LogsPaneTitle;
        Title = _baseTitle;
        SetScheme(SchemeFactory.Pane());

        _textView = new MouseClipboardTextView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ReadOnly = true,
            Multiline = true,
            WordWrap = true,
            CanFocus = true,
            ScrollBars = true,
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
        string snapshot;
        lock (_gate)
        {
            _buffer.AddLast(line);
            while (_buffer.Count > _capacity)
                _buffer.RemoveFirst();
            snapshot = string.Join('\n', _buffer);
        }
        _dispatcher.Invoke(() =>
        {
            _textView.Text = snapshot;
            _textView.MoveEnd();
        });
    }

    public void Clear()
    {
        lock (_gate) _buffer.Clear();
        _dispatcher.Invoke(() => _textView.Text = string.Empty);
    }

    /// <summary>
    /// Updates the frame title to advertise the active visible log level.
    /// Called by <see cref="StatusBarBuilder"/> after every F2 / Shift+F2 keystroke.
    /// Title format: <c>"Logs — Level: Information"</c>.
    /// </summary>
    public void SetVisibleLevel(LogLevel level)
    {
        _dispatcher.Invoke(() => Title = $"{_baseTitle} — Level: {level}");
    }

    /// <summary>Test-only accessor for the rendered TextView text.</summary>
    // Terminal.Gui's TextView normalizes line endings to "\r\n" on the Text round-trip;
    // tests assert on the logical content ("\n"), so strip the inserted CRs.
    internal string CurrentText => _textView.Text.Replace("\r\n", "\n", StringComparison.Ordinal);

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
