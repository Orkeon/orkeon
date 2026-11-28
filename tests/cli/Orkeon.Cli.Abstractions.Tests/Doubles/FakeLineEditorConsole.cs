using Orkeon.Cli.Abstractions.Console.LineEditing;

namespace Orkeon.Cli.Abstractions.Tests.Doubles;

/// <summary>An <see cref="ILineEditorConsole"/> that serves queued keys and draws nothing.</summary>
public sealed class FakeLineEditorConsole(IEnumerable<ConsoleKeyInfo> keys) : ILineEditorConsole
{
    private readonly Queue<ConsoleKeyInfo> _keys = new(keys);

    /// <summary>The keys that type <paramref name="text"/> then press Enter.</summary>
    public static IEnumerable<ConsoleKeyInfo> Typing(string text) =>
        text.Select(c => new ConsoleKeyInfo(c, ConsoleKey.NoName, shift: false, alt: false, control: false))
            .Append(new ConsoleKeyInfo('\r', ConsoleKey.Enter, shift: false, alt: false, control: false));

    public int Width => 200;

    public (int Left, int Top) GetCursor() => (0, 0);

    public void SetCursor(int left, int top) { }

    public void Write(string text) { }

    public void WriteLine(string text) { }

    public ConsoleKeyInfo ReadKey() => _keys.Dequeue();
}
