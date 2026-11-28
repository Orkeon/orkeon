using System.Text;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Abstractions.Runners;

namespace Orkeon.Cli.Abstractions.Tests.Doubles;

/// <summary>
/// A console that serves queued lines then the end of the input, keeps what is written, and says whether its lines
/// were typed (<see cref="Replayed"/> false) or came from a replay file.
/// </summary>
public sealed class StubLineConsole : IConsoleAdapter, IReplLineOrigin
{
    private readonly Queue<string> _lines;
    private readonly StringBuilder _output = new();
    private readonly Lock _gate = new();

    public StubLineConsole(params string[] lines) => _lines = new Queue<string>(lines);

    /// <summary>Whether the lines served count as replayed.</summary>
    public bool Replayed { get; set; }

    /// <inheritdoc />
    public bool LastLineReplayed => Replayed;

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
