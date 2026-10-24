using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Cli.TerminalGui.Layout;
using Orkeon.Cli.TerminalGui.Tests.Doubles;
using Orkeon.Cli.TerminalGui.Tests.Layout;

namespace Orkeon.Cli.TerminalGui.Tests.Hosting;

/// <summary>
/// The terminal capabilities reach the TUI through <see cref="ITerminalProbe"/>: the UTF-8
/// decision, the redirection probes and the OSC 52 stream are read from the probe given, never
/// from the process console.
/// </summary>
[Collection(TerminalGuiApplicationCollection.Name)]
public sealed class TerminalProbeTests
{
    [Fact]
    public void ShouldReadTheProbe_WhenAskedForUtf8()
    {
        Assert.True(TerminalUtf8.IsUtf8(new FakeTerminalProbe { Utf8 = true }));
        Assert.False(TerminalUtf8.IsUtf8(new FakeTerminalProbe { Utf8 = false }));
    }

    [Fact]
    public void ShouldReportNoUtf8_WhenTheEncodingCannotBeRead()
    {
        Assert.False(TerminalUtf8.IsUtf8(new FakeTerminalProbe { ThrowOnEncoding = true }));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ShouldNotBeInteractive_WhenTheProbeReportsARedirection(bool input, bool output)
    {
        var probe = new FakeTerminalProbe { InputRedirected = input, OutputRedirected = output };

        Assert.False(TtyDetector.IsInteractiveTty(probe));
        Assert.Equal(UiMode.Plain, TtyDetector.ResolveEffectiveMode(null, probe));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ShouldPickGlyphsFromTheProbe_WhenTheGlyphModeIsAuto(bool utf8)
    {
        var options = new TerminalGuiOptions { Terminal = new FakeTerminalProbe { Utf8 = utf8 } };

        using var pane = new ReplPaneView(options, InlineDispatcher.Instance);

        Assert.Same(utf8 ? GlyphSet.Unicode : GlyphSet.Ascii, pane.Glyphs);
    }

    [Fact]
    public void ShouldSkipOsc52AndKeepTheText_WhenNoTerminalWriterIsSet()
    {
        TuiClipboard.Reset();
        TuiClipboard.TerminalWriter = null;
        try
        {
            Assert.True(TuiClipboard.Copy("texte"));
            Assert.True(TuiClipboard.TryGetText(out var text));
            Assert.Equal("texte", text);
        }
        finally { TuiClipboard.Reset(); }
    }

    [Fact]
    public void ShouldWriteOsc52ToTheGivenWriter_WhenATerminalWriterIsSet()
    {
        using var writer = new StringWriter();
        TuiClipboard.Reset();
        TuiClipboard.TerminalWriter = writer;
        try
        {
            TuiClipboard.Copy("x");

            Assert.StartsWith("\x1b]52;c;", writer.ToString(), StringComparison.Ordinal);
        }
        finally { TuiClipboard.Reset(); TuiClipboard.TerminalWriter = null; }
    }
}
