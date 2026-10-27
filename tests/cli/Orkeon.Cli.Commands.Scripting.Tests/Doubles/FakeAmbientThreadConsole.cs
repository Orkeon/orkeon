using Orkeon.Cli.Abstractions.Console;

namespace Orkeon.Cli.Commands.Scripting.Tests.Doubles;

/// <summary>
/// <see cref="IConsoleAdapter"/> recording every line with the ambient <see cref="ConsoleThread.Current"/> of the
/// flow that wrote it, as the split-pane console routes a plain write (null: main).
/// </summary>
internal sealed class FakeAmbientThreadConsole : IConsoleAdapter
{
    private readonly List<(string? Thread, string Text)> _lines = [];
    private readonly Lock _lock = new();

    public IReadOnlyList<(string? Thread, string Text)> Lines
    {
        get { lock (_lock) return _lines.ToList(); }
    }

    public void Write(string text) => Record(text);

    public void WriteLine(string text) => Record(text);

    public string? ReadLine() => null;

    public ConsoleKeyInfo ReadKey(bool intercept = false) => default;

    public void Clear()
    {
    }

    private void Record(string text)
    {
        lock (_lock) _lines.Add((ConsoleThread.Current, text));
    }
}
