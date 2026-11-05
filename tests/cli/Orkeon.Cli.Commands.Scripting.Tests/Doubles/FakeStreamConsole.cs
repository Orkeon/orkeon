using System.Text;
using Orkeon.Cli.Abstractions.Console;

namespace Orkeon.Cli.Commands.Scripting.Tests.Doubles;

/// <summary><see cref="IStreamConsole"/> recording what reaches each of its two ways of writing.</summary>
internal sealed class FakeStreamConsole : IConsoleAdapter, IStreamConsole
{
    private readonly StringBuilder _written = new();
    private readonly StringBuilder _streamed = new();

    /// <summary>Text received by <see cref="Write"/>.</summary>
    public string Written => _written.ToString();

    /// <summary>Text received by <see cref="WriteStream"/>.</summary>
    public string Streamed => _streamed.ToString();

    public void Write(string text) => _written.Append(text);

    public void WriteStream(string text) => _streamed.Append(text);

    public void WriteLine(string text)
    {
    }

    public string? ReadLine() => null;

    public ConsoleKeyInfo ReadKey(bool intercept = false) => default;

    public void Clear()
    {
    }
}
