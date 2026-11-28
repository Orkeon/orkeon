using System.Text;
using Orkeon.Cli.Abstractions.Console;

namespace Orkeon.Cli.Commands.Scripting.Tests.Doubles;

/// <summary>A console that keeps every line written to it and reads nothing.</summary>
internal sealed class FakeLineConsole : IConsoleAdapter
{
    private readonly StringBuilder _output = new();

    public string Output => _output.ToString();

    public void Write(string text) => _output.Append(text);

    public void WriteLine(string text) => _output.Append(text).Append('\n');

    public string? ReadLine() => null;

    public ConsoleKeyInfo ReadKey(bool intercept = false) => default;

    public void Clear() => _output.Clear();
}
