using System.Text;
using Orkeon.Cli.Abstractions.Console;

namespace Orkeon.Cli.Commands.Scripting.Tests.Doubles;

/// <summary>
/// <see cref="IConsoleAdapter"/> and <see cref="IThreadedConsole"/> recording every whole line with the
/// thread it was written to (null: main). Plain adapter lines are recorded as main.
/// </summary>
internal sealed class FakeThreadedConsole : IConsoleAdapter, IThreadedConsole
{
    private readonly StringBuilder _output = new();

    public bool SeparatesThreads { get; set; } = true;

    public List<(string? Thread, string Text)> Lines { get; } = [];

    public List<(string? Thread, string Text)> Attentions { get; } = [];

    /// <summary>The number of lines written through <see cref="IThreadedConsole.WriteLine"/>.</summary>
    public int ThreadedCalls { get; private set; }

    /// <summary>Text written by <see cref="Write"/> (inline streaming).</summary>
    public string Output => _output.ToString();

    public void Write(string text) => _output.Append(text);

    public void WriteLine(string text) => Lines.Add((null, text));

    void IThreadedConsole.WriteLine(string? thread, string text)
    {
        ThreadedCalls++;
        Lines.Add((thread, text));
    }

    public void WriteAttention(string? thread, string text) => Attentions.Add((thread, text));

    public string? ReadLine() => null;

    public ConsoleKeyInfo ReadKey(bool intercept = false) => default;

    public void Clear()
    {
    }
}
