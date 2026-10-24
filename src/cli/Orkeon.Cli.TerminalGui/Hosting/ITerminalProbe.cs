namespace Orkeon.Cli.TerminalGui.Hosting;

/// <summary>
/// The terminal capabilities the text interfaces read and set, beyond what
/// <see cref="Orkeon.Cli.Abstractions.Console.IConsoleAdapter"/> carries: the redirection
/// probes, the output encoding, the Ctrl+C mode, the raw terminal stream (OSC 52) and the
/// diagnostic stream. <see cref="SystemTerminalProbe"/> is the one implementation over the
/// process console; tests pass a double.
/// </summary>
public interface ITerminalProbe
{
    /// <summary>True when standard input is not a terminal (a pipe or a file).</summary>
    bool IsInputRedirected { get; }

    /// <summary>True when standard output is not a terminal (a pipe or a file).</summary>
    bool IsOutputRedirected { get; }

    /// <summary>
    /// True when the terminal output is encoded in UTF-8 (code page 65001), which decides
    /// whether the Unicode glyphs can be drawn. May throw <see cref="IOException"/> when no
    /// console is attached; <see cref="TerminalUtf8.IsUtf8"/> reads it safely.
    /// </summary>
    bool OutputIsUtf8 { get; }

    /// <summary>
    /// Whether Ctrl+C reaches the application as a keystroke instead of raising SIGINT.
    /// Setting it may throw on hosts that refuse the mode.
    /// </summary>
    bool TreatControlCAsInput { get; set; }

    /// <summary>The terminal's own output stream: escape sequences such as OSC 52 go there.</summary>
    TextWriter Output { get; }

    /// <summary>The diagnostic stream (standard error for the system terminal).</summary>
    TextWriter Diagnostics { get; }
}

/// <summary>The single reading of <see cref="ITerminalProbe.OutputIsUtf8"/> every view shares.</summary>
public static class TerminalUtf8
{
    /// <summary>
    /// True when <paramref name="terminal"/> reports a UTF-8 output; false when it does not
    /// or when the encoding cannot be read (no console attached).
    /// </summary>
    public static bool IsUtf8(ITerminalProbe terminal)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        try
        {
            return terminal.OutputIsUtf8;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
