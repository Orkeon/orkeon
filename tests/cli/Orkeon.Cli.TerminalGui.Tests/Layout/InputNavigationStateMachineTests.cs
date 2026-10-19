using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Cli.TerminalGui.Layout;

namespace Orkeon.Cli.TerminalGui.Tests.Layout;

public class InputNavigationStateMachineTests
{
    private static readonly AgentRowInfo Main = new() { Name = "main", IsActive = true };
    private static readonly AgentRowInfo Alpha = new() { Name = "alpha@crew", Ticket = "T-1" };
    private static readonly AgentRowInfo Beta = new() { Name = "beta@crew", Ticket = "T-2" };
    private static readonly AgentRowInfo[] Tasks = [Main, Alpha, Beta];

    private static InputNavigationContext Ctx(
        string text = "",
        bool first = true,
        bool last = true,
        IReadOnlyList<AgentRowInfo>? rows = null,
        InputCaret caret = default,
        bool canSend = false,
        string? character = null,
        bool modal = false)
        => new()
        {
            Text = text,
            Caret = caret,
            CaretOnFirstRow = first,
            CaretOnLastRow = last,
            Rows = rows ?? [],
            CanSendToTask = canSend,
            Character = character,
            ModalFocused = modal,
        };

    private static InputNavigationStateMachine WithHistory(params string[] entries)
    {
        var machine = new InputNavigationStateMachine();
        foreach (var entry in entries) machine.RecordSubmitted(entry);
        return machine;
    }

    // ── Rule 0: the list is reachable only with a task other than main ──────────────────

    [Fact]
    public void List_is_available_only_with_a_ticketed_row()
    {
        Assert.False(InputNavigationStateMachine.ListAvailable([]));
        Assert.False(InputNavigationStateMachine.ListAvailable([Main]));
        Assert.True(InputNavigationStateMachine.ListAvailable(Tasks));
    }

    // ── Field ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Rule1_down_on_the_last_row_focuses_the_list()
    {
        var machine = new InputNavigationStateMachine();
        var action = machine.Handle(InputNavigationKey.Down, Ctx("typed", rows: Tasks));
        Assert.Equal(InputNavigationActionKind.FocusList, action.Kind);
        Assert.Equal(InputNavigationState.List, machine.State);
    }

    [Fact]
    public void Rule1_down_without_a_task_does_nothing()
    {
        var machine = new InputNavigationStateMachine();
        var action = machine.Handle(InputNavigationKey.Down, Ctx("typed", rows: [Main]));
        Assert.Equal(InputNavigationActionKind.None, action.Kind);
        Assert.Equal(InputNavigationState.Field, machine.State);
    }

    [Fact]
    public void Rule1_down_above_the_last_row_is_left_to_the_caret()
    {
        var machine = new InputNavigationStateMachine();
        var action = machine.Handle(InputNavigationKey.Down, Ctx("a\nb", last: false, rows: Tasks));
        Assert.False(action.Handled);
        Assert.Equal(InputNavigationState.Field, machine.State);
    }

    [Fact]
    public void Rule2_up_enters_the_history_on_the_newest_entry_and_keeps_the_draft()
    {
        var machine = WithHistory("one", "two");
        var action = machine.Handle(InputNavigationKey.Up, Ctx("half-typed"));
        Assert.Equal(InputNavigationActionKind.SetText, action.Kind);
        Assert.Equal("two", action.Text);
        Assert.Equal(InputNavigationState.History, machine.State);
        Assert.Equal("half-typed", machine.Draft);
        Assert.Equal(1, machine.HistoryIndex);
    }

    [Fact]
    public void Rule2_up_with_an_empty_history_is_not_handled()
    {
        var machine = new InputNavigationStateMachine();
        var action = machine.Handle(InputNavigationKey.Up, Ctx("x"));
        Assert.False(action.Handled);
        Assert.Equal(InputNavigationState.Field, machine.State);
    }

    [Fact]
    public void Rule2_up_below_the_first_row_is_left_to_the_caret()
    {
        var machine = WithHistory("one");
        var action = machine.Handle(InputNavigationKey.Up, Ctx("a\nb", first: false));
        Assert.False(action.Handled);
        Assert.Equal(InputNavigationState.Field, machine.State);
    }

    [Theory]
    [InlineData("Home")]
    [InlineData("End")]
    [InlineData("CtrlHome")]
    [InlineData("CtrlEnd")]
    public void Rule3_home_and_end_are_the_fields_own(string key)
    {
        var machine = new InputNavigationStateMachine();
        Assert.False(machine.Handle(Enum.Parse<InputNavigationKey>(key), Ctx("x", rows: Tasks)).Handled);
    }

    [Fact]
    public void Rule4_enter_submits_to_main_and_records_the_entry()
    {
        var machine = new InputNavigationStateMachine();
        var action = machine.Handle(InputNavigationKey.Enter, Ctx("verify 4"));
        Assert.Equal(InputNavigationActionKind.Submit, action.Kind);
        Assert.Equal("verify 4", action.Text);
        Assert.Equal(["verify 4"], machine.History);
    }

    [Fact]
    public void Blank_entries_and_consecutive_duplicates_are_not_recorded()
    {
        var machine = WithHistory("a", "a", "  ", "", "b", "a");
        Assert.Equal(["a", "b", "a"], machine.History);
    }

    // ── History ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Rule5_up_walks_older_entries_and_stops_on_the_oldest()
    {
        var machine = WithHistory("one", "two");
        machine.Handle(InputNavigationKey.Up, Ctx());
        Assert.Equal("one", machine.Handle(InputNavigationKey.Up, Ctx("two")).Text);
        var atOldest = machine.Handle(InputNavigationKey.Up, Ctx("one"));
        Assert.Equal(InputNavigationActionKind.None, atOldest.Kind);
        Assert.Equal(0, machine.HistoryIndex);
    }

    [Fact]
    public void Rule5_up_in_history_ignores_the_caret_row()
    {
        var machine = WithHistory("one", "two\nlines");
        machine.Handle(InputNavigationKey.Up, Ctx());
        var action = machine.Handle(InputNavigationKey.Up, Ctx("two\nlines", first: false));
        Assert.Equal("one", action.Text);
    }

    [Fact]
    public void Rule6_down_from_the_newest_returns_the_draft_then_a_second_down_reaches_the_list()
    {
        var machine = WithHistory("one", "two");
        machine.Handle(InputNavigationKey.Up, Ctx("draft", rows: Tasks));
        machine.Handle(InputNavigationKey.Up, Ctx("two", rows: Tasks));
        Assert.Equal("two", machine.Handle(InputNavigationKey.Down, Ctx("one", rows: Tasks)).Text);

        var back = machine.Handle(InputNavigationKey.Down, Ctx("two", rows: Tasks));
        Assert.Equal(InputNavigationActionKind.SetText, back.Kind);
        Assert.Equal("draft", back.Text);
        Assert.Equal(InputNavigationState.Field, machine.State);

        var list = machine.Handle(InputNavigationKey.Down, Ctx("draft", rows: Tasks));
        Assert.Equal(InputNavigationActionKind.FocusList, list.Kind);
    }

    [Fact]
    public void Rule7_an_edit_in_history_leaves_the_mode_and_keeps_the_text()
    {
        var machine = WithHistory("one");
        machine.Handle(InputNavigationKey.Up, Ctx("draft"));

        machine.OnTextEdited("one!");

        Assert.Equal(InputNavigationState.Field, machine.State);
        Assert.Equal("one!", machine.Draft);
        // The next ↑ enters the history again from the edited text.
        machine.Handle(InputNavigationKey.Up, Ctx("one!"));
        Assert.Equal("one!", machine.Draft);
    }

    [Fact]
    public void Rule8_enter_in_history_submits_the_shown_entry_and_drops_the_draft()
    {
        var machine = WithHistory("one", "two");
        machine.Handle(InputNavigationKey.Up, Ctx("draft"));
        machine.Handle(InputNavigationKey.Up, Ctx("two"));

        var action = machine.Handle(InputNavigationKey.Enter, Ctx("one"));

        Assert.Equal(InputNavigationActionKind.Submit, action.Kind);
        Assert.Equal("one", action.Text);
        Assert.Equal(InputNavigationState.Field, machine.State);
        Assert.Equal(string.Empty, machine.Draft);
        Assert.Equal(["one", "two", "one"], machine.History);
    }

    [Fact]
    public void Rule9_home_and_end_in_history_keep_the_mode()
    {
        var machine = WithHistory("one");
        machine.Handle(InputNavigationKey.Up, Ctx("draft"));
        Assert.False(machine.Handle(InputNavigationKey.Home, Ctx("one")).Handled);
        Assert.False(machine.Handle(InputNavigationKey.CtrlEnd, Ctx("one")).Handled);
        Assert.Equal(InputNavigationState.History, machine.State);
    }

    [Fact]
    public void Escape_clear_resets_the_history_mode()
    {
        var machine = WithHistory("one");
        machine.Handle(InputNavigationKey.Up, Ctx("draft"));
        Assert.False(machine.Handle(InputNavigationKey.Escape, Ctx("one")).Handled);
        machine.Reset();
        Assert.Equal(InputNavigationState.Field, machine.State);
        Assert.Equal(string.Empty, machine.Draft);
    }

    // ── List ─────────────────────────────────────────────────────────────────────────────

    private static InputNavigationStateMachine InList(string text = "typed", InputCaret caret = default)
    {
        var machine = new InputNavigationStateMachine();
        machine.Handle(InputNavigationKey.Down, Ctx(text, rows: Tasks, caret: caret));
        return machine;
    }

    [Fact]
    public void Rule10_down_moves_to_the_next_row_without_wrapping()
    {
        var machine = InList();
        Assert.Null(machine.SelectedTicket);
        Assert.Equal("T-1", machine.Handle(InputNavigationKey.Down, Ctx(rows: Tasks)).Ticket);
        Assert.Equal("T-2", machine.Handle(InputNavigationKey.Down, Ctx(rows: Tasks)).Ticket);

        var atLast = machine.Handle(InputNavigationKey.Down, Ctx(rows: Tasks));

        Assert.Equal(InputNavigationActionKind.None, atLast.Kind);
        Assert.Equal("T-2", machine.SelectedTicket);
    }

    [Fact]
    public void Rule11_up_on_the_first_row_restores_the_field_text_and_caret()
    {
        var machine = InList("kept text", new InputCaret(4, 0));
        machine.Handle(InputNavigationKey.Down, Ctx(rows: Tasks));
        machine.Handle(InputNavigationKey.Up, Ctx(rows: Tasks));

        var action = machine.Handle(InputNavigationKey.Up, Ctx(rows: Tasks));

        Assert.Equal(InputNavigationActionKind.FocusField, action.Kind);
        Assert.Equal("kept text", action.Text);
        Assert.Equal(new InputCaret(4, 0), action.Caret);
        Assert.Equal(InputNavigationState.Field, machine.State);
    }

    [Fact]
    public void Rule11_escape_returns_to_the_field_from_anywhere_in_the_list()
    {
        var machine = InList("kept", new InputCaret(2, 0));
        machine.Handle(InputNavigationKey.End, Ctx(rows: Tasks));

        var action = machine.Handle(InputNavigationKey.Escape, Ctx(rows: Tasks));

        Assert.Equal(InputNavigationActionKind.FocusField, action.Kind);
        Assert.Equal("kept", action.Text);
        Assert.Equal(new InputCaret(2, 0), action.Caret);
    }

    [Fact]
    public void Rule12_the_selected_row_becomes_the_recipient_and_main_restores_the_default()
    {
        var machine = InList();
        machine.Handle(InputNavigationKey.Down, Ctx(rows: Tasks));
        Assert.Equal("T-1", machine.RecipientTicket);
        Assert.Equal("alpha@crew", machine.RecipientName);

        machine.Handle(InputNavigationKey.Escape, Ctx(rows: Tasks));
        Assert.Equal("T-1", machine.RecipientTicket);

        // Back in the list, the selection starts on the recipient.
        Assert.Equal("T-1", machine.Handle(InputNavigationKey.Down, Ctx(rows: Tasks)).Ticket);
        machine.Handle(InputNavigationKey.Up, Ctx(rows: Tasks));
        Assert.Null(machine.RecipientTicket);
        Assert.Null(machine.RecipientName);
    }

    [Fact]
    public void Rule12_a_task_recipient_without_the_port_keeps_the_text_and_reports()
    {
        var machine = InList();
        machine.Handle(InputNavigationKey.Down, Ctx(rows: Tasks));
        machine.Handle(InputNavigationKey.Escape, Ctx(rows: Tasks));

        var action = machine.Handle(InputNavigationKey.Enter, Ctx("hello task", rows: Tasks));

        Assert.Equal(InputNavigationActionKind.RecipientUnsupported, action.Kind);
        Assert.Equal("T-1", action.Ticket);
        Assert.Empty(machine.History);
    }

    [Fact]
    public void Rule12_a_task_recipient_with_the_port_sends_to_the_task()
    {
        var machine = InList();
        machine.Handle(InputNavigationKey.Down, Ctx(rows: Tasks));
        machine.Handle(InputNavigationKey.Escape, Ctx(rows: Tasks));

        var action = machine.Handle(InputNavigationKey.Enter, Ctx("hello task", rows: Tasks, canSend: true));

        Assert.Equal(InputNavigationActionKind.SendToTask, action.Kind);
        Assert.Equal("T-1", action.Ticket);
        Assert.Equal("hello task", action.Text);
    }

    [Fact]
    public void Rule13_enter_opens_the_selected_task_and_sends_nothing()
    {
        var machine = InList();
        Assert.Equal(InputNavigationActionKind.None, machine.Handle(InputNavigationKey.Enter, Ctx(rows: Tasks)).Kind);
        machine.Handle(InputNavigationKey.Down, Ctx(rows: Tasks));

        var action = machine.Handle(InputNavigationKey.Enter, Ctx(rows: Tasks));

        Assert.Equal(InputNavigationActionKind.OpenTask, action.Kind);
        Assert.Equal("T-1", action.Ticket);
        Assert.Equal(InputNavigationState.List, machine.State);
        Assert.Empty(machine.History);
    }

    [Fact]
    public void Rule14_an_emptied_list_returns_to_the_field_with_main_as_recipient()
    {
        var machine = InList("kept");
        machine.Handle(InputNavigationKey.Down, Ctx(rows: Tasks));

        var action = machine.OnRowsChanged([]);

        Assert.Equal(InputNavigationActionKind.FocusField, action.Kind);
        Assert.Equal("kept", action.Text);
        Assert.Equal(InputNavigationState.Field, machine.State);
        Assert.Null(machine.RecipientTicket);
    }

    [Fact]
    public void A_resort_keeps_the_selection_on_its_ticket()
    {
        var machine = InList();
        machine.Handle(InputNavigationKey.Down, Ctx(rows: Tasks));

        var action = machine.OnRowsChanged([Main, Beta, Alpha]);

        Assert.Equal(InputNavigationActionKind.None, action.Kind);
        Assert.Equal("T-1", machine.SelectedTicket);
        Assert.Equal("T-2", machine.Handle(InputNavigationKey.Up, Ctx(rows: [Main, Beta, Alpha])).Ticket);
    }

    [Fact]
    public void A_vanished_selection_moves_to_the_row_at_its_place()
    {
        var machine = InList();
        machine.Handle(InputNavigationKey.End, Ctx(rows: Tasks));

        var action = machine.OnRowsChanged([Main, Alpha]);

        Assert.Equal(InputNavigationActionKind.MoveListSelection, action.Kind);
        Assert.Equal("T-1", machine.SelectedTicket);
        Assert.Equal("T-1", machine.RecipientTicket);
    }

    [Fact]
    public void A_recipient_task_that_left_the_list_gives_way_to_main()
    {
        var machine = InList();
        machine.Handle(InputNavigationKey.Down, Ctx(rows: Tasks));
        machine.Handle(InputNavigationKey.Escape, Ctx(rows: Tasks));

        machine.OnRowsChanged([Main, Beta]);

        Assert.Null(machine.RecipientTicket);
    }

    [Fact]
    public void Rule15_a_printable_key_returns_to_the_field_with_the_character()
    {
        var machine = InList("ab", new InputCaret(2, 0));
        var action = machine.Handle(InputNavigationKey.Printable, Ctx(rows: Tasks, character: "c"));
        Assert.Equal(InputNavigationActionKind.FocusField, action.Kind);
        Assert.Equal("ab", action.Text);
        Assert.Equal("c", action.Insert);
    }

    [Fact]
    public void Rule15_home_and_end_reach_the_first_and_last_rows()
    {
        var machine = InList();
        Assert.Equal("T-2", machine.Handle(InputNavigationKey.End, Ctx(rows: Tasks)).Ticket);
        var home = machine.Handle(InputNavigationKey.Home, Ctx(rows: Tasks));
        Assert.Equal(InputNavigationActionKind.MoveListSelection, home.Kind);
        Assert.Null(home.Ticket);
    }

    [Fact]
    public void Toggle_enters_the_list_from_the_history_and_returns_to_it()
    {
        var machine = WithHistory("one");
        machine.Handle(InputNavigationKey.Up, Ctx("draft", rows: Tasks));

        Assert.Equal(InputNavigationActionKind.FocusList, machine.Handle(InputNavigationKey.ToggleList, Ctx("one", rows: Tasks)).Kind);
        var back = machine.Handle(InputNavigationKey.ToggleList, Ctx(rows: Tasks));

        Assert.Equal("one", back.Text);
        Assert.Equal(InputNavigationState.History, machine.State);
        Assert.Equal("draft", machine.Handle(InputNavigationKey.Down, Ctx("one", rows: Tasks)).Text);
    }

    [Fact]
    public void A_focused_modal_dialog_takes_every_key()
    {
        var machine = WithHistory("one");
        foreach (var key in Enum.GetValues<InputNavigationKey>())
            Assert.False(machine.Handle(key, Ctx("x", rows: Tasks, modal: true)).Handled);
        Assert.Equal(InputNavigationState.Field, machine.State);
    }
}
