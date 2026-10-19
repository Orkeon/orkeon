using Orkeon.Cli.TerminalGui.Hosting;

namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>Where the prompt's navigation keys are interpreted.</summary>
internal enum InputNavigationState
{
    /// <summary>Typing in the input field.</summary>
    Field,

    /// <summary>Walking the submitted entries; the typed text waits as the draft.</summary>
    History,

    /// <summary>The tasks list owns the keys.</summary>
    List,
}

/// <summary>The keys the machine interprets; every other key stays the views' own.</summary>
internal enum InputNavigationKey
{
    Up,
    Down,
    Home,
    End,
    CtrlHome,
    CtrlEnd,
    Enter,
    Escape,
    Printable,
    ToggleList,

    /// <summary>Tab: completion in the field; from the list, back to the field.</summary>
    Tab,

    /// <summary>A paste (Ctrl+V, right-click) is about to land in the field.</summary>
    Paste,
}

/// <summary>A caret position in the input field (column, row), as the field reports it.</summary>
internal readonly record struct InputCaret(int Column, int Row);

/// <summary>What the views know when a key arrives.</summary>
internal sealed record InputNavigationContext
{
    /// <summary>The field's text, line endings normalised to <c>\n</c>.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>The field's caret.</summary>
    public InputCaret Caret { get; init; }

    /// <summary>True when the caret is on the first VISUAL (wrapped) row of the field.</summary>
    public bool CaretOnFirstRow { get; init; } = true;

    /// <summary>True when the caret is on the last VISUAL (wrapped) row of the field.</summary>
    public bool CaretOnLastRow { get; init; } = true;

    /// <summary>The tasks list's rows, in display order (<c>main</c> carries no ticket).</summary>
    public IReadOnlyList<AgentRowInfo> Rows { get; init; } = [];

    /// <summary>True when a modal dialog has focus: the machine then handles nothing.</summary>
    public bool ModalFocused { get; init; }

    /// <summary>True when the host can deliver a message to a task.</summary>
    public bool CanSendToTask { get; init; }

    /// <summary>The typed character of a <see cref="InputNavigationKey.Printable"/> key.</summary>
    public string? Character { get; init; }
}

/// <summary>What the views must do after a key.</summary>
internal enum InputNavigationActionKind
{
    /// <summary>The machine leaves the key to the focused view's native handling.</summary>
    NotHandled,

    /// <summary>The key is consumed and nothing changes on screen.</summary>
    None,

    /// <summary>Focus the tasks list on <see cref="InputNavigationAction.Ticket"/>.</summary>
    FocusList,

    /// <summary>Move the list's selection to <see cref="InputNavigationAction.Ticket"/>.</summary>
    MoveListSelection,

    /// <summary>Focus the field, restore <see cref="InputNavigationAction.Text"/> and the caret, then insert <see cref="InputNavigationAction.Insert"/>.</summary>
    FocusField,

    /// <summary>Show <see cref="InputNavigationAction.Text"/> in the field, caret at the end.</summary>
    SetText,

    /// <summary>Submit <see cref="InputNavigationAction.Text"/> to the REPL (recipient <c>main</c>).</summary>
    Submit,

    /// <summary>Hand <see cref="InputNavigationAction.Text"/> to the task <see cref="InputNavigationAction.Ticket"/>.</summary>
    SendToTask,

    /// <summary>The recipient is a task and the host cannot deliver to it: report, keep the text.</summary>
    RecipientUnsupported,

    /// <summary>Open the view of the task <see cref="InputNavigationAction.Ticket"/>.</summary>
    OpenTask,
}

/// <summary>One instruction to the views.</summary>
internal sealed record InputNavigationAction(InputNavigationActionKind Kind)
{
    public static readonly InputNavigationAction NotHandled = new(InputNavigationActionKind.NotHandled);

    public static readonly InputNavigationAction None = new(InputNavigationActionKind.None);

    public string? Text { get; init; }

    public InputCaret? Caret { get; init; }

    /// <summary>A task's ticket; null names <c>main</c>.</summary>
    public string? Ticket { get; init; }

    /// <summary>Text to insert once the field has focus again (a key typed in the list).</summary>
    public string? Insert { get; init; }

    /// <summary>The name of the recipient task that left the rows and gave way to <c>main</c>; null otherwise.</summary>
    public string? DepartedRecipient { get; init; }

    /// <summary>True when the key was the machine's (anything but <see cref="InputNavigationActionKind.NotHandled"/>).</summary>
    public bool Handled => Kind != InputNavigationActionKind.NotHandled;
}

/// <summary>
/// The prompt's three-state key interpretation: <see cref="InputNavigationState.Field"/>,
/// <see cref="InputNavigationState.History"/> and <see cref="InputNavigationState.List"/>. A key is
/// read in the current state only, never twice. The machine is pure: the views translate their
/// events into <see cref="Handle"/> calls and apply the returned <see cref="InputNavigationAction"/>.
/// </summary>
/// <remarks>
/// History holds the submitted entries, newest last; entering it stores the typed text as the draft
/// (the virtual entry N), and ↓ from the newest entry gives it back. The list's selection is kept by
/// ticket, so a re-sort of the rows never moves it to another task, and the selected row is the
/// recipient of the next message (<c>main</c>, without a ticket, is the default recipient).
/// </remarks>
internal sealed class InputNavigationStateMachine
{
    private readonly List<string> _history = [];
    private int _historyIndex;
    private string _draft = string.Empty;
    private InputNavigationState _returnState;
    private string _savedText = string.Empty;
    private InputCaret _savedCaret;
    private string? _selectedTicket;
    private int _selectedIndex;

    /// <summary>The current state.</summary>
    public InputNavigationState State { get; private set; }

    /// <summary>The submitted entries, oldest first.</summary>
    public IReadOnlyList<string> History => _history;

    /// <summary>The shown entry while in <see cref="InputNavigationState.History"/>, else the entry count (the draft).</summary>
    public int HistoryIndex => State == InputNavigationState.History ? _historyIndex : _history.Count;

    /// <summary>The text typed before entering the history.</summary>
    public string Draft => _draft;

    /// <summary>The selected row's ticket while in the list (null: <c>main</c>).</summary>
    public string? SelectedTicket => _selectedTicket;

    /// <summary>The recipient task's ticket; null: <c>main</c>, the REPL itself.</summary>
    public string? RecipientTicket { get; private set; }

    /// <summary>The recipient task's row name; null: <c>main</c>.</summary>
    public string? RecipientName { get; private set; }

    /// <summary>True when the list is reachable: at least one row other than <c>main</c>.</summary>
    public static bool ListAvailable(IReadOnlyList<AgentRowInfo> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return rows.Any(r => !string.IsNullOrEmpty(r.Ticket));
    }

    /// <summary>Interprets <paramref name="key"/> in the current state.</summary>
    public InputNavigationAction Handle(InputNavigationKey key, InputNavigationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.ModalFocused) return InputNavigationAction.NotHandled;
        return State switch
        {
            InputNavigationState.History => HandleHistory(key, context),
            InputNavigationState.List => HandleList(key, context),
            _ => HandleField(key, context),
        };
    }

    /// <summary>
    /// The user changed the field's text (typing, paste, completion): in the history, the mode
    /// ends and the edited text, which stays in the field, becomes the draft — nothing is lost.
    /// </summary>
    public void OnTextEdited(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (State != InputNavigationState.History) return;
        State = InputNavigationState.Field;
        _draft = text;
    }

    /// <summary>Esc cleared the field: back to <see cref="InputNavigationState.Field"/>, no draft.</summary>
    public void Reset()
    {
        if (State == InputNavigationState.History) State = InputNavigationState.Field;
        _draft = string.Empty;
    }

    /// <summary>
    /// Records a delivered entry: blank entries and a repeat of the newest one are not kept.
    /// The mode ends and the draft is dropped.
    /// </summary>
    public void RecordSubmitted(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Trim().Length > 0 && (_history.Count == 0 || !string.Equals(_history[^1], text, StringComparison.Ordinal)))
            _history.Add(text);
        if (State == InputNavigationState.History) State = InputNavigationState.Field;
        _draft = string.Empty;
    }

    /// <summary>
    /// The list's rows changed (1 Hz poll). An emptied list hands the focus back to the field with
    /// <c>main</c> as recipient; a vanished selection moves to the row now at its place; a recipient
    /// task that left the list gives way to <c>main</c>. When the recipient falls back to <c>main</c>
    /// that way, the action names the departed task (<see cref="InputNavigationAction.DepartedRecipient"/>).
    /// </summary>
    public InputNavigationAction OnRowsChanged(IReadOnlyList<AgentRowInfo> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (State == InputNavigationState.List)
        {
            if (!ListAvailable(rows))
            {
                var departed = RecipientName;
                SetRecipient(null, null);
                return LeaveList(null) with { DepartedRecipient = departed };
            }
            var index = IndexOf(rows, _selectedTicket);
            if (index >= 0)
            {
                _selectedIndex = index;
                return InputNavigationAction.None;
            }
            return Select(rows, Math.Clamp(_selectedIndex, 0, rows.Count - 1));
        }

        if (RecipientTicket is null || IndexOf(rows, RecipientTicket) >= 0) return InputNavigationAction.None;
        var name = RecipientName;
        SetRecipient(null, null);
        return new InputNavigationAction(InputNavigationActionKind.None) { DepartedRecipient = name };
    }

    /// <summary>
    /// The field took the focus by another way than the machine's own (a mouse click, a focus
    /// move, an edit landing in it): the list is left, and the field keeps the text and the caret
    /// it has now (the returned action carries neither). Outside the list, nothing happens.
    /// </summary>
    public InputNavigationAction OnFieldFocused()
    {
        if (State != InputNavigationState.List) return InputNavigationAction.None;
        State = _returnState;
        _selectedTicket = null;
        return new InputNavigationAction(InputNavigationActionKind.FocusField);
    }

    /// <summary>
    /// A click on the row of <paramref name="ticket"/> (null: <c>main</c>): from the field it enters
    /// the list (the text and caret are saved as ↓ saves them) on that row; in the list it moves the
    /// selection there. A ticket the rows do not hold, or a list with no task, changes nothing.
    /// </summary>
    public InputNavigationAction SelectRow(string? ticket, InputNavigationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.ModalFocused || !ListAvailable(context.Rows)) return InputNavigationAction.None;
        var index = IndexOf(context.Rows, NormaliseTicket(ticket));
        if (index < 0) return InputNavigationAction.None;
        if (State == InputNavigationState.List) return Select(context.Rows, index);
        EnterList(context);
        Select(context.Rows, index);
        return new InputNavigationAction(InputNavigationActionKind.FocusList) { Ticket = _selectedTicket };
    }

    private InputNavigationAction HandleField(InputNavigationKey key, InputNavigationContext context)
    {
        switch (key)
        {
            case InputNavigationKey.Up:
                if (!context.CaretOnFirstRow || _history.Count == 0) return InputNavigationAction.NotHandled;
                _draft = context.Text;
                _historyIndex = _history.Count - 1;
                State = InputNavigationState.History;
                return ShowEntry(_history[_historyIndex]);
            case InputNavigationKey.Down:
                if (!context.CaretOnLastRow) return InputNavigationAction.NotHandled;
                return ListAvailable(context.Rows) ? EnterList(context) : InputNavigationAction.None;
            case InputNavigationKey.ToggleList:
                return ListAvailable(context.Rows) ? EnterList(context) : InputNavigationAction.None;
            case InputNavigationKey.Enter:
                return Submit(context);
            default:
                return InputNavigationAction.NotHandled;
        }
    }

    private InputNavigationAction HandleHistory(InputNavigationKey key, InputNavigationContext context)
    {
        switch (key)
        {
            case InputNavigationKey.Up:
                if (_historyIndex == 0) return InputNavigationAction.None;
                _historyIndex--;
                return ShowEntry(_history[_historyIndex]);
            case InputNavigationKey.Down:
                if (_historyIndex < _history.Count - 1)
                {
                    _historyIndex++;
                    return ShowEntry(_history[_historyIndex]);
                }
                var draft = _draft;
                _draft = string.Empty;
                State = InputNavigationState.Field;
                return ShowEntry(draft);
            case InputNavigationKey.ToggleList:
                return ListAvailable(context.Rows) ? EnterList(context) : InputNavigationAction.None;
            case InputNavigationKey.Enter:
                return Submit(context);
            default:
                return InputNavigationAction.NotHandled;
        }
    }

    private InputNavigationAction HandleList(InputNavigationKey key, InputNavigationContext context)
    {
        var rows = context.Rows;
        if (!ListAvailable(rows))
        {
            SetRecipient(null, null);
            return LeaveList(null);
        }

        var index = Math.Max(0, IndexOf(rows, _selectedTicket));
        switch (key)
        {
            case InputNavigationKey.Down:
                return index < rows.Count - 1 ? Select(rows, index + 1) : InputNavigationAction.None;
            case InputNavigationKey.Up:
                return index > 0 ? Select(rows, index - 1) : LeaveList(null);
            case InputNavigationKey.Home:
                return Select(rows, 0);
            case InputNavigationKey.End:
                return Select(rows, rows.Count - 1);
            case InputNavigationKey.Escape:
            case InputNavigationKey.ToggleList:
            case InputNavigationKey.Tab:
            case InputNavigationKey.Paste:
                return LeaveList(null);
            case InputNavigationKey.Printable:
                return string.IsNullOrEmpty(context.Character) ? InputNavigationAction.None : LeaveList(context.Character);
            case InputNavigationKey.Enter:
                return rows[index].Ticket is { Length: > 0 } ticket
                    ? new InputNavigationAction(InputNavigationActionKind.OpenTask) { Ticket = ticket }
                    : InputNavigationAction.None;
            default:
                return InputNavigationAction.None;
        }
    }

    private InputNavigationAction Submit(InputNavigationContext context)
    {
        var text = context.Text;
        if (RecipientTicket is null)
        {
            RecordSubmitted(text);
            return new InputNavigationAction(InputNavigationActionKind.Submit) { Text = text };
        }
        if (text.Trim().Length == 0) return InputNavigationAction.None;
        return context.CanSendToTask
            ? new InputNavigationAction(InputNavigationActionKind.SendToTask) { Text = text, Ticket = RecipientTicket }
            : new InputNavigationAction(InputNavigationActionKind.RecipientUnsupported) { Text = text, Ticket = RecipientTicket };
    }

    private InputNavigationAction EnterList(InputNavigationContext context)
    {
        _savedText = context.Text;
        _savedCaret = context.Caret;
        _returnState = State;
        State = InputNavigationState.List;
        var rows = context.Rows;
        var index = RecipientTicket is null ? 0 : Math.Max(0, IndexOf(rows, RecipientTicket));
        Select(rows, index);
        return new InputNavigationAction(InputNavigationActionKind.FocusList) { Ticket = _selectedTicket };
    }

    private InputNavigationAction LeaveList(string? insert)
    {
        State = _returnState;
        _selectedTicket = null;
        return new InputNavigationAction(InputNavigationActionKind.FocusField)
        {
            Text = _savedText,
            Caret = _savedCaret,
            Insert = insert,
        };
    }

    private InputNavigationAction Select(IReadOnlyList<AgentRowInfo> rows, int index)
    {
        var row = rows[index];
        _selectedIndex = index;
        _selectedTicket = NormaliseTicket(row.Ticket);
        SetRecipient(_selectedTicket, _selectedTicket is null ? null : row.Name);
        return new InputNavigationAction(InputNavigationActionKind.MoveListSelection) { Ticket = _selectedTicket };
    }

    private void SetRecipient(string? ticket, string? name)
    {
        RecipientTicket = ticket;
        RecipientName = name;
    }

    private static InputNavigationAction ShowEntry(string text)
        => new(InputNavigationActionKind.SetText) { Text = text };

    private static string? NormaliseTicket(string? ticket) => string.IsNullOrEmpty(ticket) ? null : ticket;

    private static int IndexOf(IReadOnlyList<AgentRowInfo> rows, string? ticket)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (string.Equals(NormaliseTicket(rows[i].Ticket), ticket, StringComparison.Ordinal)) return i;
        }
        return -1;
    }
}
