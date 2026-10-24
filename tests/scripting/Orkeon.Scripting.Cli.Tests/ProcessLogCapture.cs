namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// Captures what the hosts' <c>Microsoft.Extensions.Logging</c> console provider writes for the
/// duration of a scope. A verb's own output reaches the <see cref="Orkeon.Hosting.IRunnerConsole"/>
/// it is handed (<see cref="TestConsole"/>); the console logger provider of a host it builds still
/// writes to the process streams, so a test asserting on a log line swaps them here. Those streams
/// are process-global: callers hold <see cref="CliCollection"/>.
/// </summary>
internal sealed class ProcessLogCapture : IDisposable
{
    private readonly TextWriter _originalOut;
    private readonly TextWriter _originalError;
    private readonly StringWriter _out = new();
    private readonly StringWriter _error = new();

    public ProcessLogCapture()
    {
        _originalOut = Console.Out;
        _originalError = Console.Error;
        Console.SetOut(TextWriter.Synchronized(_out));
        Console.SetError(TextWriter.Synchronized(_error));
    }

    /// <summary>What was logged to the process standard output.</summary>
    public string Stdout => _out.ToString();

    /// <summary>What was logged to the process standard error.</summary>
    public string Stderr => _error.ToString();

    public void Dispose()
    {
        Console.SetOut(_originalOut);
        Console.SetError(_originalError);
        _out.Dispose();
        _error.Dispose();
    }
}
