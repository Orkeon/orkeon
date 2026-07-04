using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Abstractions.Console.LineEditing;

namespace Orkeon.Cli.Abstractions.Tests.Console;

public sealed class LineEditorTests
{
    private static readonly string[] ExitCommand = ["exit"];
    private static readonly string[] ProgramPath = ["src/Program.cs"];
    private static readonly string[] CommitCompactCommands = ["commit", "compact"];
    private static readonly string[] CmdHistory = ["cmd"];

    [Fact]
    public void Types_text_and_returns_it_on_Enter()
    {
        var editor = NewEditor(Cat(Type("hello"), Enter));
        Assert.Equal("hello", editor.ReadLine());
    }

    [Fact]
    public void Backspace_deletes_left_of_cursor()
    {
        var editor = NewEditor(Cat(Type("hi"), Key(ConsoleKey.Backspace), Enter));
        Assert.Equal("h", editor.ReadLine());
    }

    [Fact]
    public void Left_arrow_then_insert_writes_at_the_cursor()
    {
        // "ac", move left once (between a and c), type 'b' → "abc".
        var editor = NewEditor(Cat(Type("ac"), Key(ConsoleKey.LeftArrow), Type("b"), Enter));
        Assert.Equal("abc", editor.ReadLine());
    }

    [Fact]
    public void Delete_removes_char_under_cursor()
    {
        // "abc", Home, Delete → "bc".
        var editor = NewEditor(Cat(Type("abc"), Key(ConsoleKey.Home), Key(ConsoleKey.Delete), Enter));
        Assert.Equal("bc", editor.ReadLine());
    }

    [Fact]
    public void History_recalls_the_previous_line_with_CursorUp()
    {
        var editor = NewEditor(Cat(Type("first"), Enter, Key(ConsoleKey.UpArrow), Enter));
        Assert.Equal("first", editor.ReadLine());  // submit "first"
        Assert.Equal("first", editor.ReadLine());  // ↑ recalls it
    }

    [Fact]
    public void History_up_then_down_restores_the_draft()
    {
        var keys = Cat(
            Type("older"), Enter,                  // submit "older"
            Type("half"), Key(ConsoleKey.UpArrow), // draft "half", ↑ → "older"
            Key(ConsoleKey.DownArrow), Enter);     // ↓ → back to "half"
        var editor = NewEditor(keys);
        Assert.Equal("older", editor.ReadLine());
        Assert.Equal("half", editor.ReadLine());
    }

    [Fact]
    public void Tab_completes_a_unique_slash_command()
    {
        var editor = NewEditor(Cat(Type("/ex"), Key(ConsoleKey.Tab), Enter),
            assist: new FakeAssist(commands: ExitCommand));
        Assert.Equal("/exit", editor.ReadLine());
    }

    [Fact]
    public void Tab_completes_an_at_path_token()
    {
        var editor = NewEditor(Cat(Type("@src/Pro"), Key(ConsoleKey.Tab), Enter),
            assist: new FakeAssist(paths: ProgramPath));
        Assert.Equal("@src/Program.cs", editor.ReadLine());
    }

    [Fact]
    public void Tab_on_ambiguous_command_inserts_the_longest_common_prefix()
    {
        var editor = NewEditor(Cat(Type("/co"), Key(ConsoleKey.Tab), Enter),
            assist: new FakeAssist(commands: CommitCompactCommands));
        Assert.Equal("/com", editor.ReadLine());
    }

    [Fact]
    public void CtrlD_on_empty_line_returns_null()
    {
        var editor = NewEditor(new[] { CtrlD });
        Assert.Null(editor.ReadLine());
    }

    [Fact]
    public void Blank_and_duplicate_lines_are_not_recorded_in_history()
    {
        var editor = NewEditor(Cat(Type("cmd"), Enter, Type("cmd"), Enter, Key(ConsoleKey.Enter)));
        editor.ReadLine(); // "cmd"
        editor.ReadLine(); // "cmd" again (duplicate)
        editor.ReadLine(); // "" (blank)
        Assert.Equal(CmdHistory, editor.History);
    }

    // ── multi-line (Ctrl+O newline) ─────────────────────────────────────────────────────────

    [Fact]
    public void CtrlO_inserts_a_newline_without_submitting()
    {
        var editor = NewEditor(Cat(Type("first"), CtrlO, Type("second"), Enter));
        Assert.Equal("first\nsecond", editor.ReadLine());
    }

    [Fact]
    public void CtrlO_in_the_middle_splits_the_line_at_the_cursor()
    {
        // "ab", move left once (caret between a and b), Ctrl+O → "a\nb".
        var editor = NewEditor(Cat(Type("ab"), Key(ConsoleKey.LeftArrow), CtrlO, Enter));
        Assert.Equal("a\nb", editor.ReadLine());
    }

    [Fact]
    public void Backspace_at_line_start_merges_with_the_previous_line()
    {
        // "a\nb", caret at start of "b" (after Ctrl+O the caret sits before 'b'); Backspace deletes \n.
        var editor = NewEditor(Cat(Type("a"), CtrlO, Type("b"), Key(ConsoleKey.Home),
            Key(ConsoleKey.Backspace), Enter));
        Assert.Equal("ab", editor.ReadLine());
    }

    [Fact]
    public void UpArrow_inside_a_multiline_draft_moves_the_caret_instead_of_recalling_history()
    {
        // Two-line draft; ↑ from the second row moves to the first row (no history recall), then
        // typing inserts on the first line: "Xfirst\nsecond".
        var editor = NewEditor(Cat(Type("first"), CtrlO, Type("second"),
            Key(ConsoleKey.UpArrow), Key(ConsoleKey.Home), Type("X"), Enter));
        Assert.Equal("Xfirst\nsecond", editor.ReadLine());
    }

    [Fact]
    public void UpArrow_on_the_first_row_still_recalls_history()
    {
        var editor = NewEditor(Cat(Type("done"), Enter, Key(ConsoleKey.UpArrow), Enter));
        Assert.Equal("done", editor.ReadLine());  // submit
        Assert.Equal("done", editor.ReadLine());  // ↑ on the first (only) row recalls it
    }

    [Fact]
    public void Home_and_End_act_on_the_current_logical_line()
    {
        // "ab\ncd": from end of "cd", Home → start of "cd", type 'X' → "ab\nXcd".
        var editor = NewEditor(Cat(Type("ab"), CtrlO, Type("cd"),
            Key(ConsoleKey.Home), Type("X"), Enter));
        Assert.Equal("ab\nXcd", editor.ReadLine());
    }

    // ── word wrap ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Long_line_wraps_onto_the_next_visual_row()
    {
        // Width 6: "abcdefghij" must render as "abcdef" on row 0 and "ghij" on row 1, while the
        // returned (logical) line stays a single unwrapped string — no \n injected by wrapping.
        var rec = new RecordingConsole(width: 6, Cat(Type("abcdefghij"), Enter));
        var editor = new LineEditor(rec);

        Assert.Equal("abcdefghij", editor.ReadLine());
        Assert.Contains(rec.Writes, w => w.Top == 1 && w.Text == "ghij");
    }

    [Fact]
    public void Wrapping_accounts_for_the_prompt_anchor_on_the_first_row()
    {
        // Anchor at column 3 (as if "ab>" was already printed). Width 6 ⇒ row 0 holds only 3 chars
        // ("xyz") before the wrap, then "wv" on row 1.
        var rec = new RecordingConsole(width: 6, Cat(Type("xyzwv"), Enter)) { CursorLeft = 3 };
        var editor = new LineEditor(rec);

        Assert.Equal("xyzwv", editor.ReadLine());
        Assert.Contains(rec.Writes, w => w.Top == 1 && w.Text == "wv");
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────

    private static LineEditor NewEditor(IEnumerable<ConsoleKeyInfo> keys, IReplInputAssist? assist = null)
        => new(new FakeConsole(keys), assist);

    private static ConsoleKeyInfo[] Cat(params IEnumerable<ConsoleKeyInfo>[] parts)
        => parts.SelectMany(p => p).ToArray();

    private static ConsoleKeyInfo[] Type(string s)
        => s.Select(c => new ConsoleKeyInfo(c, (ConsoleKey)0, false, false, false)).ToArray();

    private static ConsoleKeyInfo[] Key(ConsoleKey key)
        => new[] { new ConsoleKeyInfo('\0', key, false, false, false) };

    private static ConsoleKeyInfo[] Enter => Key(ConsoleKey.Enter);

    private static readonly ConsoleKeyInfo CtrlD = new('\u0004', ConsoleKey.D, false, false, control: true);

    private static readonly ConsoleKeyInfo[] CtrlO =
        { new('', ConsoleKey.O, shift: false, alt: false, control: true) };

    /// <summary>
    /// Fake console with a configurable width that records every non-empty <see cref="Write"/> as
    /// (Left, Top, Text), tracking cursor moves via <see cref="SetCursor"/>. Lets wrap tests assert on
    /// the visual rows the editor laid out.
    /// </summary>
    private sealed class RecordingConsole : ILineEditorConsole
    {
        private readonly Queue<ConsoleKeyInfo> _keys;

        public RecordingConsole(int width, IEnumerable<ConsoleKeyInfo> keys)
        {
            Width = width;
            _keys = new Queue<ConsoleKeyInfo>(keys);
        }

        public int Width { get; }
        public int CursorLeft { get; set; }
        public int CursorTop { get; set; }
        public List<(int Left, int Top, string Text)> Writes { get; } = new();

        public (int Left, int Top) GetCursor() => (CursorLeft, CursorTop);
        public void SetCursor(int left, int top) { CursorLeft = left; CursorTop = top; }

        public void Write(string text)
        {
            if (text.Length > 0) Writes.Add((CursorLeft, CursorTop, text));
            CursorLeft += text.Length;
        }

        public void WriteLine(string text)
        {
            if (text.Length > 0) Writes.Add((CursorLeft, CursorTop, text));
            CursorLeft = 0;
            CursorTop++;
        }

        public ConsoleKeyInfo ReadKey() => _keys.Dequeue();
    }

    private sealed class FakeConsole : ILineEditorConsole
    {
        private readonly Queue<ConsoleKeyInfo> _keys;
        public FakeConsole(IEnumerable<ConsoleKeyInfo> keys) => _keys = new Queue<ConsoleKeyInfo>(keys);

        public int Width => 200;
        public (int Left, int Top) GetCursor() => (0, 0);
        public void SetCursor(int left, int top) { }
        public void Write(string text) { }
        public void WriteLine(string text) { }
        public ConsoleKeyInfo ReadKey() => _keys.Dequeue();
    }

    private sealed class FakeAssist : IReplInputAssist
    {
        private readonly string[] _commands;
        private readonly string[] _paths;

        public FakeAssist(string[]? commands = null, string[]? paths = null)
        {
            _commands = commands ?? Array.Empty<string>();
            _paths = paths ?? Array.Empty<string>();
        }

        public IReadOnlyList<string> CompleteCommand(string prefix)
            => _commands.Where(c => c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();

        public IReadOnlyList<string> CompletePath(string prefix)
        {
            var leaf = prefix.Contains('/') ? prefix[(prefix.LastIndexOf('/') + 1)..] : prefix;
            return _paths
                .Where(p => (p.Contains('/') ? p[(p.LastIndexOf('/') + 1)..] : p)
                    .StartsWith(leaf, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }
}
