using System.Text;
using Terminal.Gui.App;

namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>
/// Hybrid clipboard for the TUI, built for terminals where Terminal.Gui's OS clipboard
/// integration is unavailable (Docker/SSH: no xclip/X11, no powershell.exe — the driver
/// silently falls back to an in-process fake). Copy fans out to every reachable surface;
/// paste falls back to the last in-process copy:
/// <list type="bullet">
///   <item>OS clipboard via <see cref="Application.Clipboard"/> when supported (WSL, X11,
///   macOS, Windows).</item>
///   <item>OSC 52 escape sequence — the hosting terminal emulator (Windows Terminal, iTerm2,
///   kitty, …) sets the <em>host</em> clipboard even across docker/ssh boundaries. Emission is
///   best-effort and skipped for payloads beyond <see cref="MaxOsc52Base64Length"/> (terminals
///   cap the sequence length; an oversize one would be dropped or garble the display).</item>
///   <item>An in-process cache, so copy → paste round-trips inside the TUI even with no OS
///   integration at all. (Reading the host clipboard back via OSC 52 is refused by most
///   terminals for security — paste-from-host arrives through the terminal's own paste,
///   i.e. bracketed paste / Ctrl+Shift+V, which Terminal.Gui inserts natively.)</item>
/// </list>
/// </summary>
internal static class TuiClipboard
{
    /// <summary>
    /// Cap on the base64 payload of the OSC 52 sequence (~74 KiB of UTF-8 text) — aligned
    /// with xterm's historical 100 KB total-sequence limit, the lowest common denominator.
    /// </summary>
    internal const int MaxOsc52Base64Length = 99_000;

    private static string? _lastCopy;

    /// <summary>Test hook: replaces the terminal writer used for OSC 52 (defaults to stdout).</summary>
    internal static TextWriter? TerminalWriterOverride { get; set; }

    /// <summary>
    /// Copies <paramref name="text"/> to every reachable clipboard surface (in-process cache,
    /// OS clipboard when supported, host terminal via OSC 52). Returns false only for
    /// null/empty text — a non-empty copy always succeeds somewhere (at least the cache).
    /// </summary>
    public static bool Copy(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        _lastCopy = text;
        Application.Clipboard?.TrySetClipboardData(text);
        EmitOsc52(text);
        return true;
    }

    /// <summary>
    /// Reads the best available clipboard content: the OS clipboard when it is supported and
    /// non-empty, else the last in-process copy. Returns false when both are empty.
    /// </summary>
    public static bool TryGetText(out string text)
    {
        var os = Application.Clipboard;
        if (os is not null && os.TryGetClipboardData(out var osText) && !string.IsNullOrEmpty(osText))
        {
            text = osText;
            return true;
        }
        if (!string.IsNullOrEmpty(_lastCopy))
        {
            text = _lastCopy;
            return true;
        }
        text = string.Empty;
        return false;
    }

    /// <summary>
    /// Clears every surface <see cref="TryGetText"/> reads (tests): the in-process cache and
    /// the driver clipboard. The latter matters because it outlives a test — once another
    /// test class has run <c>Application.Init</c>, Terminal.Gui's <c>FakeClipboard</c> keeps
    /// whatever the previous test copied, and a test asserting "nothing to paste" read it
    /// back (flaky in CI, 2026-09-11). Best-effort: an unsupported clipboard refuses the
    /// write and is equally invisible to <see cref="TryGetText"/>.
    /// </summary>
    internal static void Reset()
    {
        _lastCopy = null;
        Application.Clipboard?.TrySetClipboardData(string.Empty);
    }

    private static void EmitOsc52(string text)
    {
        try
        {
            var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
            if (payload.Length > MaxOsc52Base64Length)
                return;
            // Fully qualified: `Console` alone resolves to the Orkeon.Cli.TerminalGui.Console namespace.
            var writer = TerminalWriterOverride ?? System.Console.Out;
            // OSC 52 ; c(lipboard) ; <base64> BEL — atomic write + flush so the sequence never
            // interleaves with a partially-emitted TUI frame.
            writer.Write($"\x1b]52;c;{payload}\a");
            writer.Flush();
        }
        catch (IOException)
        {
            // stdout gone (detached tty) — copy still landed in the cache/OS clipboard.
        }
    }
}
