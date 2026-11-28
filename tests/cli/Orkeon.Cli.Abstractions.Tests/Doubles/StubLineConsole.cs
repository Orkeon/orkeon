using System.Text;
using Orkeon.Cli.Abstractions.Console;

namespace Orkeon.Cli.Abstractions.Tests.Doubles;

/// <summary>
/// A console that serves queued lines then the end of the input and keeps what is written. It says nothing of where
/// its lines came from — piped input, a feed of a host's own —, so a line that starts with <c>!</c> never runs a
/// command; <see cref="StubTypedLineConsole"/> is the one that states its lines were typed.
/// </summary>
public class StubLineConsole : IConsoleAdapter
{
    private readonly Queue<string> _lines;
    private readonly StringBuilder _output = new();
    private readonly Lock _gate = new();

    public StubLineConsole(params string[] lines) => _lines = new Queue<string>(lines);

    public string Output
    {
        get { lock (_gate) return _output.ToString(); }
    }

    public void Write(string text) { lock (_gate) _output.Append(text); }

    public void WriteLine(string text) { lock (_gate) _output.Append(text).Append('\n'); }

    public string? ReadLine() => _lines.Count == 0 ? null : _lines.Dequeue();

    public ConsoleKeyInfo ReadKey(bool intercept = false) => throw new InvalidOperationException("No key is queued.");

    public void Clear() { lock (_gate) _output.Clear(); }
}
