using System.Text;
using System.Threading.Channels;
using Orkeon.Cli.Abstractions.Console;

namespace Orkeon.Cli.Commands.Scripting.Tests.Fixtures;

/// <summary>
/// Minimal <see cref="IConsoleAdapter"/> for scripted REPL-style tests in this project.
/// Drives input lines through a channel and accumulates output in a buffer.
/// </summary>
/// <remarks>
/// This is a Phase 1 stand-in for the richer <c>TestConsoleAdapter</c> living in
/// <c>tests/cli/Orkeon.Cli.Abstractions.Tests/Console/</c>. Promoting that adapter to
/// <c>Orkeon.Tests.Shared</c> would touch ~15 existing test files and was deferred —
/// see <c>project/tasks/CLI-TS-PLAN.md</c> Q1.
/// </remarks>
internal sealed class ScriptedTestConsole : IConsoleAdapter
{
    private readonly Channel<string> _inputs = Channel.CreateUnbounded<string>();
    private readonly StringBuilder _output = new();
    private readonly object _lock = new();

    public void Write(string text)
    {
        lock (_lock) _output.Append(text);
    }

    public void WriteLine(string text)
    {
        lock (_lock) _output.AppendLine(text);
    }

    public string? ReadLine()
    {
        try
        {
            return _inputs.Reader.ReadAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (ChannelClosedException)
        {
            return null;
        }
    }

    public ConsoleKeyInfo ReadKey(bool intercept = false) => default;

    public void Clear()
    {
        lock (_lock) _output.Clear();
    }

    public void EnqueueLine(string line) => _inputs.Writer.TryWrite(line);

    public void CompleteInput() => _inputs.Writer.TryComplete();

    public string Output
    {
        get { lock (_lock) return _output.ToString(); }
    }
}
