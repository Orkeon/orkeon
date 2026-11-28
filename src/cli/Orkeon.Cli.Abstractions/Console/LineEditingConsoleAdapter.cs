using Orkeon.Compliance.Vfs;
using Orkeon.Cli.Abstractions.Console.LineEditing;
using Orkeon.Cli.Abstractions.Runners;

namespace Orkeon.Cli.Abstractions.Console;

/// <summary>
/// Interactive plain-mode console adapter: delegates output to <see cref="System.Console"/> but
/// reads input through a raw-mode <see cref="LineEditor"/> (history ↑/↓, Tab completion of
/// <c>/command</c> and <c>@path</c>). Selected over <see cref="SystemConsoleAdapter"/> only when the
/// process owns an interactive TTY; when stdin is redirected it transparently falls back to
/// <see cref="System.Console.ReadLine"/> so pipes and tests stay byte-exact. It states a line was typed
/// (<see cref="LastLineTyped"/>) only when its editor read it from the keyboard.
/// </summary>
[SuppressConsoleCompliance("CONSOLE-ADAPTER: IConsoleAdapter over the process console, with line editing.")]
public sealed class LineEditingConsoleAdapter : IConsoleAdapter, IReplLineOrigin
{
    private readonly LineEditor _editor;
    private readonly Func<bool> _isInputRedirected;
    private readonly Func<string?> _readRedirected;
    private volatile bool _lastLineTyped;

    public LineEditingConsoleAdapter(IReplInputAssist? assist = null)
        : this(new LineEditor(new SystemLineEditorConsole(), assist), static () => System.Console.IsInputRedirected, System.Console.ReadLine)
    {
    }

    /// <summary>The seam the tests use: the editor, the redirection probe and the read of a redirected input.</summary>
    internal LineEditingConsoleAdapter(LineEditor editor, Func<bool> isInputRedirected, Func<string?> readRedirected)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _isInputRedirected = isInputRedirected ?? throw new ArgumentNullException(nameof(isInputRedirected));
        _readRedirected = readRedirected ?? throw new ArgumentNullException(nameof(readRedirected));
    }

    /// <inheritdoc />
    public bool LastLineTyped => _lastLineTyped;

    public void Write(string text) => System.Console.Write(text);

    public void WriteLine(string text) => System.Console.WriteLine(text);

    public string? ReadLine()
    {
        _lastLineTyped = false;
        if (_isInputRedirected())
            return _readRedirected();

        var line = _editor.ReadLine();
        _lastLineTyped = line is not null;
        return line;
    }

    public ConsoleKeyInfo ReadKey(bool intercept = false) => System.Console.ReadKey(intercept);

    public void Clear() => System.Console.Clear();
}
