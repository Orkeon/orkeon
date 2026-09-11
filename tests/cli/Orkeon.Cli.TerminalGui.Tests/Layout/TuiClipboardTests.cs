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
public sealed class TuiClipboardTests : IDisposable
{
    public TuiClipboardTests() => TuiClipboard.Reset();

    public void Dispose()
    {
        TuiClipboard.Reset();
        TuiClipboard.TerminalWriterOverride = null;
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Copy_then_TryGetText_round_trips_through_the_cache()
    {
        TuiClipboard.TerminalWriterOverride = new StringWriter();

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
        TuiClipboard.TerminalWriterOverride = writer;

        TuiClipboard.Copy("héllo ── monde");

        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("héllo ── monde"));
        Assert.Equal($"\x1b]52;c;{expected}\a", writer.ToString());
    }

    [Fact]
    public void Copy_skips_osc52_for_oversize_payloads_but_still_caches()
    {
        var writer = new StringWriter();
        TuiClipboard.TerminalWriterOverride = writer;
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
        TuiClipboard.TerminalWriterOverride = new StringWriter();
        TuiClipboard.Copy("COLLÉ\nligne 2");
        using var input = new MouseClipboardTextView { Text = "", ReadOnly = false };

        Assert.True(PaneClipboard.PasteInto(input));

        Assert.Equal("COLLÉ\nligne 2", input.Text.Replace("\r\n", "\n"));
    }

    [Fact]
    public void PasteInto_returns_false_when_every_surface_is_empty()
    {
        using var input = new MouseClipboardTextView { Text = "seed", ReadOnly = false };
        Assert.False(PaneClipboard.PasteInto(input));
        Assert.Equal("seed", input.Text);
    }
}
