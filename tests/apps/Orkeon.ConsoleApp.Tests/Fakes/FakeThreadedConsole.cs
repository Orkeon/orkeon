using Orkeon.Cli.Abstractions.Console;

namespace Orkeon.ConsoleApp.Tests.Fakes;

/// <summary>Records the thread-aware writes it receives; <see cref="SeparatesThreads"/> is set by the test.</summary>
internal sealed class FakeThreadedConsole : IConsoleAdapter, IThreadedConsole, IStreamConsole, IBlockConsole
{
    public bool SeparatesThreads { get; set; }

    public List<(string? Thread, string Text)> Lines { get; } = [];

    public List<(string? Thread, string Text)> Attentions { get; } = [];

    public List<string> PlainLines { get; } = [];

    public List<(ConsoleBlockKind Kind, string Text)> Blocks { get; } = [];

    public void WriteBlock(ConsoleBlockKind kind, string text) => Blocks.Add((kind, text));

    public List<string> Streamed { get; } = [];

    public void WriteStream(string text) => Streamed.Add(text);

    public void Write(string text) => PlainLines.Add(text);

    public void WriteLine(string text) => PlainLines.Add(text);

    public void WriteLine(string? thread, string text) => Lines.Add((thread, text));

    /// <summary>Raised after an attention line is recorded, on the writer's thread: a test waits on it rather than
    /// reading <see cref="Attentions"/> while another thread still writes there.</summary>
    public event Action<(string? Thread, string Text)>? AttentionWritten;

    public void WriteAttention(string? thread, string text)
    {
        Attentions.Add((thread, text));
        AttentionWritten?.Invoke((thread, text));
    }

    public string? ReadLine() => null;

    public ConsoleKeyInfo ReadKey(bool intercept = false) => default;

    public void Clear() => PlainLines.Clear();
}
