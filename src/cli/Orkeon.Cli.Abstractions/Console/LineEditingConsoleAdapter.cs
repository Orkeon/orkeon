using Orkeon.Cli.Abstractions.Console.LineEditing;

namespace Orkeon.Cli.Abstractions.Console;

/// <summary>
/// Interactive plain-mode console adapter: delegates output to <see cref="System.Console"/> but
/// reads input through a raw-mode <see cref="LineEditor"/> (history ↑/↓, Tab completion of
/// <c>/command</c> and <c>@path</c>). Selected over <see cref="SystemConsoleAdapter"/> only when the
/// process owns an interactive TTY; when stdin is redirected it transparently falls back to
/// <see cref="System.Console.ReadLine"/> so pipes and tests stay byte-exact.
/// </summary>
public sealed class LineEditingConsoleAdapter : IConsoleAdapter
{
    private readonly LineEditor _editor;

    public LineEditingConsoleAdapter(IReplInputAssist? assist = null)
    {
        _editor = new LineEditor(new SystemLineEditorConsole(), assist);
    }

    public void Write(string text) => System.Console.Write(text);

    public void WriteLine(string text) => System.Console.WriteLine(text);

    public string? ReadLine()
        => System.Console.IsInputRedirected ? System.Console.ReadLine() : _editor.ReadLine();

    public ConsoleKeyInfo ReadKey(bool intercept = false) => System.Console.ReadKey(intercept);

    public void Clear() => System.Console.Clear();
}
