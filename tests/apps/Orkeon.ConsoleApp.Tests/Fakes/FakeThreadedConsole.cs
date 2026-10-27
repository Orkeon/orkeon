using Orkeon.Cli.Abstractions.Console;

namespace Orkeon.ConsoleApp.Tests.Fakes;

/// <summary>Records the thread-aware writes it receives; <see cref="SeparatesThreads"/> is set by the test.</summary>
internal sealed class FakeThreadedConsole : IConsoleAdapter, IThreadedConsole
{
    public bool SeparatesThreads { get; set; }

    public List<(string? Thread, string Text)> Lines { get; } = [];

    public List<(string? Thread, string Text)> Attentions { get; } = [];

    public List<string> PlainLines { get; } = [];

    public void Write(string text) => PlainLines.Add(text);

    public void WriteLine(string text) => PlainLines.Add(text);

    public void WriteLine(string? thread, string text) => Lines.Add((thread, text));

    public void WriteAttention(string? thread, string text) => Attentions.Add((thread, text));

    public string? ReadLine() => null;

    public ConsoleKeyInfo ReadKey(bool intercept = false) => default;

    public void Clear() => PlainLines.Clear();
}
