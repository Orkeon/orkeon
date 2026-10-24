using Orkeon.Cli.Abstractions.Console;

namespace Orkeon.ConsoleApp.Tests.Fakes;

/// <summary>Records what is written; reads are not scripted.</summary>
internal sealed class FakeConsoleAdapter : IConsoleAdapter
{
    public List<string> Lines { get; } = [];

    public void Write(string text) => Lines.Add(text);

    public void WriteLine(string text) => Lines.Add(text);

    public string? ReadLine() => null;

    public ConsoleKeyInfo ReadKey(bool intercept = false) => default;

    public void Clear() => Lines.Clear();
}
