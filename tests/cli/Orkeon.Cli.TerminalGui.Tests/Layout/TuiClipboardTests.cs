using System.Drawing;
using System.Text;
using Orkeon.Cli.TerminalGui.Layout;

namespace Orkeon.Cli.TerminalGui.Tests.Layout;

/// <summary>
/// Deterministic, OS-clipboard-independent behaviour of <see cref="TuiClipboard"/>: the
/// in-process cache round-trip (the paste fallback used when xclip/pbcopy are absent —
/// exactly the headless-CI / Docker situation) and the OSC 52 emission captured through
/// the test writer hook. Sequential by nature (shared static cache, and a driver clipboard
/// that survives whichever test class ran <c>Application.Init</c>): every test resets both.
/// </summary>
[Collection(TerminalGuiApplicationCollection.Name)]
public sealed class TuiClipboardTests : IDisposable
{
    public TuiClipboardTests() => TuiClipboard.Reset();

    public void Dispose()
    {
        TuiClipboard.Reset();
        TuiClipboard.TerminalWriter = null;
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Copy_then_TryGetText_round_trips_through_the_cache()
    {
        TuiClipboard.TerminalWriter = new StringWriter();

        Assert.True(TuiClipboard.Copy("ligne 1\nligne 2"));
        Assert.True(TuiClipboard.TryGetText(out var text));
        Assert.Equal("ligne 1\nligne 2", text);
    }

    [Fact]
    public void Copy_rejects_null_and_empty()
    {
        Assert.False(TuiClipboard.Copy(null));
        Assert.False(TuiClipboard.Copy(""));
        Assert.False(TuiClipboard.TryGetText(out _));
    }

    [Fact]
    public void Copy_emits_a_well_formed_osc52_sequence()
    {
        var writer = new StringWriter();
        TuiClipboard.TerminalWriter = writer;

        TuiClipboard.Copy("héllo ── monde");

        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("héllo ── monde"));
        Assert.Equal($"\x1b]52;c;{expected}\a", writer.ToString());
    }

    [Fact]
    public void Copy_skips_osc52_for_oversize_payloads_but_still_caches()
    {
        var writer = new StringWriter();
        TuiClipboard.TerminalWriter = writer;
        // Base64 grows by 4/3: this is comfortably past MaxOsc52Base64Length once encoded.
        var big = new string('x', TuiClipboard.MaxOsc52Base64Length);

        Assert.True(TuiClipboard.Copy(big));

        Assert.Equal(string.Empty, writer.ToString());
        Assert.True(TuiClipboard.TryGetText(out var text));
        Assert.Equal(big, text);
    }

    [Fact]
    public void PasteInto_inserts_the_cached_copy_at_the_caret_multiline_included()
    {
        TuiClipboard.TerminalWriter = new StringWriter();
        TuiClipboard.Copy("COLLÉ\nligne 2");
        using var input = new MouseClipboardTextView { Text = "", ReadOnly = false };

        Assert.True(PaneClipboard.PasteInto(input));

        Assert.Equal("COLLÉ\nligne 2", input.Text.Replace("\r\n", "\n"));
    }

    [Fact]
    public void PasteInto_inserts_at_the_caret_inside_a_line_and_moves_the_caret_past_the_paste()
    {
        TuiClipboard.TerminalWriter = new StringWriter();
        TuiClipboard.Copy("é+");
        using var input = new MouseClipboardTextView { Text = "ab😀cd", ReadOnly = false };
        // Columns are runes: the emoji is one column but two UTF-16 chars.
        input.InsertionPoint = new Point(3, 0);

        Assert.True(PaneClipboard.PasteInto(input));

        Assert.Equal("ab😀é+cd", input.Text);
        Assert.Equal(new Point(5, 0), input.InsertionPoint);
    }

    [Fact]
    public void PasteInto_multiline_lands_the_caret_at_the_end_of_the_pasted_text()
    {
        TuiClipboard.TerminalWriter = new StringWriter();
        TuiClipboard.Copy("un\r\ndeux\r\ntrois");
        using var input = new MouseClipboardTextView { Text = "[]", ReadOnly = false };
        input.InsertionPoint = new Point(1, 0);

        Assert.True(PaneClipboard.PasteInto(input));

        Assert.Equal("[un\ndeux\ntrois]", input.Text.Replace("\r\n", "\n"));
        Assert.Equal(new Point(5, 2), input.InsertionPoint);
    }

    [Fact]
    public void PasteInto_a_whole_transcript_is_one_text_change()
    {
        // The rune-by-rune insertion of TextView.InsertText froze the TUI for minutes on a paste of
        // two hundred lines (one change event per rune): the paste now sets the text once.
        TuiClipboard.TerminalWriter = new StringWriter();
        var transcript = string.Join('\n', Enumerable.Range(1, 200).Select(i => $"ligne {i} " + new string('x', 70)));
        TuiClipboard.Copy(transcript);
        using var input = new MouseClipboardTextView { Text = "", ReadOnly = false };
        var changes = 0;
        input.ContentsChanged += (_, _) => changes++;

        Assert.True(PaneClipboard.PasteInto(input));

        Assert.Equal(transcript, input.Text.Replace("\r\n", "\n"));
        Assert.Equal(1, changes);
        Assert.Equal(new Point(80, 199), input.InsertionPoint);
    }

    [Fact]
    public void PasteInto_returns_false_when_every_surface_is_empty()
    {
        using var input = new MouseClipboardTextView { Text = "seed", ReadOnly = false };
        Assert.False(PaneClipboard.PasteInto(input));
        Assert.Equal("seed", input.Text);
    }
}
