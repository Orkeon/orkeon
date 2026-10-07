namespace Orkeon.Scripting.Cli.Tests.Doubles;

/// <summary>
/// A standard input a test writes to while the verb runs — the pipe of a program that drives the
/// CLI. A read blocks its thread until a line is written or the input is closed, exactly as a
/// read of a redirected <see cref="Console.In"/> does; closing it is the end of input.
/// </summary>
internal sealed class ScriptedStandardInput : TextReader
{
    private readonly object _gate = new();
    private readonly Queue<string> _lines = new();
    private bool _closed;

    /// <summary>Writes one line the verb will read.</summary>
    public void WriteLine(string line)
    {
        lock (_gate)
        {
            _lines.Enqueue(line);
            Monitor.PulseAll(_gate);
        }
    }

    /// <inheritdoc />
    public override string? ReadLine()
    {
        lock (_gate)
        {
            while (_lines.Count == 0 && !_closed)
                Monitor.Wait(_gate);

            // Closed and drained: the end of input.
            return _lines.Count > 0 ? _lines.Dequeue() : null;
        }
    }

    /// <inheritdoc />
    public override Task<string?> ReadLineAsync() => Task.FromResult(ReadLine());

    /// <inheritdoc />
    public override ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken) => new(ReadLine());

    /// <summary>Closes the input: every read, blocked or to come, answers null once the lines are drained.</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_gate)
            {
                _closed = true;
                Monitor.PulseAll(_gate);
            }
        }

        base.Dispose(disposing);
    }
}
