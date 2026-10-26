using System.Drawing;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.Text;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>
/// Read-only, line-based transcript view backed by a bounded <see cref="TranscriptBuffer"/> (issue #196).
/// It replaces a read-only <c>TextView</c> whose every append re-parsed and re-wrapped the whole text:
/// appending here wraps only the lines added, trimming the head drops lines without touching the others,
/// and drawing walks only the rows in the viewport.
/// </summary>
/// <remarks>
/// Kept from the text view it replaces: display-only soft wrap (copy returns the source text, no injected
/// line break), vertical and horizontal scrolling with the built-in scrollbars, mouse drag selection in
/// inverse video with auto-copy on release (<see cref="TuiClipboard"/>), Ctrl+A / Ctrl+C and the
/// navigation keys (arrows, Page Up / Page Down, Home / End) when focused, and tabs expanded to the next
/// stop of <see cref="TranscriptBuffer.TabWidth"/> columns (a copy keeps the tab). The autoscroll is a
/// state, not a one-shot scroll: <see cref="MoveEnd"/> sticks the view to its tail, applied again when
/// rows are added or the view is resized or laid out (a pane shown later), until the user scrolls up.
/// Not kept: double-click word selection and the auto-scroll while dragging a selection.
/// Must be touched on the UI thread only.
/// </remarks>
internal sealed class TranscriptView : View
{
    private const int WheelRows = 3;

    private readonly TranscriptBuffer _buffer;
    private bool _wordWrap = true;
    private TranscriptPosition? _anchor;
    private TranscriptPosition _caret;
    private bool _dragging;
    private bool _followTail = true;

    public TranscriptView(int capacity)
    {
        _buffer = new TranscriptBuffer(capacity);
        ViewportSettings |= ViewportSettingsFlags.HasVerticalScrollBar;
    }

    /// <summary>Display-only soft wrap. Toggling it re-wraps every line once.</summary>
    public bool WordWrap
    {
        get => _wordWrap;
        set
        {
            if (_wordWrap == value) return;
            _wordWrap = value;
            if (value) ViewportSettings &= ~ViewportSettingsFlags.HasHorizontalScrollBar;
            else ViewportSettings |= ViewportSettingsFlags.HasHorizontalScrollBar;
            Relayout();
        }
    }

    /// <summary>The whole transcript, lines joined by <c>\n</c>.</summary>
    public string TranscriptText => _buffer.Text;

    /// <summary>Lines currently held (bounded by the capacity).</summary>
    public int LineCount => _buffer.LineCount;

    /// <summary>The selected text, empty when nothing is selected.</summary>
    public string SelectedText => _anchor is { } anchor ? _buffer.GetText(anchor, _caret) : string.Empty;

    /// <summary>The model, for tests that count the wrap work.</summary>
    internal TranscriptBuffer Buffer => _buffer;

    /// <summary>Appends stream text (a fragment without <c>\n</c> extends the open last line).</summary>
    public void Append(string text)
    {
        _buffer.Append(text);
        ContentChanged();
    }

    /// <summary>Appends each item as its own line.</summary>
    public void AppendLines(IEnumerable<string> lines)
    {
        _buffer.AppendLines(lines);
        ContentChanged();
    }

    public void Clear()
    {
        _buffer.Clear();
        _anchor = null;
        ContentChanged();
    }

    /// <summary>
    /// Scrolls to the last row and sticks there: the view keeps showing the tail when rows are added
    /// or when its size changes (a pane laid out or shown later), until the user scrolls up.
    /// </summary>
    public void MoveEnd()
    {
        _followTail = true;
        FollowTail();
    }

    // Applies the stick-to-the-end state; a no-op while the view has no height yet.
    private void FollowTail()
    {
        var height = Viewport.Height;
        if (!_followTail || height <= 0) return;
        var y = Math.Max(0, _buffer.RowCount - height);
        if (Viewport.Y != y) Viewport = Viewport with { Y = y };
    }

    public void SelectAll()
    {
        _anchor = _buffer.Start;
        _caret = _buffer.End;
        SetNeedsDraw();
    }

    /// <inheritdoc />
    protected override void OnViewportChanged(DrawEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnViewportChanged(e);
        if (_buffer.Width != Viewport.Width) Relayout();
        if (e.OldViewport.Size != e.NewViewport.Size) FollowTail();
        else if (e.OldViewport.Y != e.NewViewport.Y)
            // A scroll (keys, wheel, scrollbar): the view follows the tail again once back at the end.
            _followTail = Viewport.Y >= _buffer.RowCount - Viewport.Height;
    }

    /// <inheritdoc />
    protected override bool OnDrawingContent(DrawContext? context)
    {
        var normal = GetAttributeForRole(VisualRole.Normal);
        var width = Viewport.Width;
        var selection = SelectionRange();
        for (var y = 0; y < Viewport.Height; y++)
        {
            SetAttribute(normal);
            Move(0, y);
            AddStr(new string(' ', Math.Max(0, width)));
            if (_buffer.GetRow(Viewport.Y + y) is not { } row) continue;
            DrawRow(y, row, width, normal, selection);
        }
        return true;
    }

    private void DrawRow(int y, TranscriptRow row, int width, Attribute normal, (TranscriptPosition From, TranscriptPosition To)? selection)
    {
        var skip = _wordWrap ? 0 : Viewport.X;
        var column = 0;
        var offset = row.Start;
        foreach (var grapheme in GraphemeHelper.GetGraphemes(row.Text[row.Start..row.End]))
        {
            // A tab is drawn as the blanks up to the next stop of its row.
            var w = TranscriptBuffer.ColumnsOf(grapheme, column);
            var at = column - skip;
            column += w;
            var position = new TranscriptPosition(row.Line, offset);
            offset += grapheme.Length;
            if (at < 0) continue;
            if (at + w > width) break;
            var selected = selection is { } range
                && position.CompareTo(range.From) >= 0 && position.CompareTo(range.To) < 0;
            SetAttribute(selected ? MouseClipboardTextView.SelectionAttribute : normal);
            Move(at, y);
            AddStr(grapheme == "\t" ? new string(' ', w) : grapheme);
        }
    }

    /// <inheritdoc />
    protected override bool OnMouseEvent(Mouse mouse)
    {
        ArgumentNullException.ThrowIfNull(mouse);
        var flags = mouse.Flags;
        if (flags.HasFlag(MouseFlags.WheeledUp)) return Scroll(-WheelRows);
        if (flags.HasFlag(MouseFlags.WheeledDown)) return Scroll(WheelRows);

        var point = mouse.Position ?? Point.Empty;
        var position = _buffer.HitTest(Viewport.Y + point.Y, Viewport.X + point.X);
        if (flags.HasFlag(MouseFlags.LeftButtonPressed))
        {
            if (!_dragging)
            {
                _dragging = true;
                _anchor = position;
                App?.Mouse.GrabMouse(this);
            }
            _caret = position;
            SetNeedsDraw();
            return true;
        }

        if (flags.HasFlag(MouseFlags.LeftButtonReleased) || flags.HasFlag(MouseFlags.LeftButtonClicked))
        {
            if (_dragging)
            {
                _dragging = false;
                _caret = position;
                App?.Mouse.UngrabMouse();
            }
            var selected = SelectedText;
            if (selected.Length > 0) TuiClipboard.Copy(selected);
            else _anchor = null;
            SetNeedsDraw();
            return true;
        }

        return base.OnMouseEvent(mouse);
    }

    /// <inheritdoc />
    protected override bool OnKeyDown(Key key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (PaneClipboard.IsCtrl(key, KeyCode.A))
        {
            SelectAll();
            return true;
        }
        if (PaneClipboard.IsCtrl(key, KeyCode.C) && PaneClipboard.CopySelection(this)) return true;

        var page = Math.Max(1, Viewport.Height - 1);
        return key.KeyCode switch
        {
            KeyCode.CursorUp => Scroll(-1),
            KeyCode.CursorDown => Scroll(1),
            KeyCode.PageUp => Scroll(-page),
            KeyCode.PageDown => Scroll(page),
            KeyCode.Home => Scroll(-_buffer.RowCount),
            KeyCode.End => Scroll(_buffer.RowCount),
            _ => base.OnKeyDown(key),
        };
    }

    private bool Scroll(int rows)
    {
        var max = Math.Max(0, _buffer.RowCount - Math.Max(0, Viewport.Height));
        var y = Math.Clamp(Viewport.Y + rows, 0, max);
        if (y != Viewport.Y) Viewport = Viewport with { Y = y };
        return true;
    }

    private (TranscriptPosition From, TranscriptPosition To)? SelectionRange()
    {
        if (_anchor is not { } anchor || anchor.CompareTo(_caret) == 0) return null;
        return anchor.CompareTo(_caret) < 0 ? (anchor, _caret) : (_caret, anchor);
    }

    private void Relayout()
    {
        _buffer.SetLayout(Viewport.Width, _wordWrap);
        ContentChanged();
    }

    private void ContentChanged()
    {
        var width = _wordWrap ? Math.Max(0, Viewport.Width) : _buffer.MaxColumns;
        SetContentSize(new Size(width, _buffer.RowCount));
        FollowTail();
        SetNeedsDraw();
    }
}
