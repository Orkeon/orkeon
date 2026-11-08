using System.Text;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// A <see cref="TextWriter"/> that is not thread-safe on purpose, as a process's stdout is not: it
/// yields in the middle of every write, so two writes that overlap are caught
/// (<see cref="MaxConcurrentWrites"/>) and would garble <see cref="Lines"/>.
/// </summary>
public sealed class FakeLineWriter : TextWriter
{
    private readonly StringBuilder _text = new();
    private int _inFlight;
    private int _maxInFlight;

    /// <inheritdoc />
    public override Encoding Encoding => Encoding.UTF8;

    /// <summary>The most writes that ran at the same moment.</summary>
    public int MaxConcurrentWrites => Volatile.Read(ref _maxInFlight);

    /// <summary>Everything written so far, split into lines.</summary>
    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (_text)
                return _text.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        }
    }

    /// <inheritdoc />
    public override Task WriteAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default) =>
        AppendAsync(buffer.ToString());

    /// <inheritdoc />
    public override Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default) =>
        AppendAsync(buffer + "\n");

    /// <inheritdoc />
    public override Task WriteAsync(string? value) => AppendAsync(value ?? string.Empty);

    /// <inheritdoc />
    public override Task WriteLineAsync(string? value) => AppendAsync((value ?? string.Empty) + "\n");

    /// <inheritdoc />
    public override Task FlushAsync() => Task.CompletedTask;

    private async Task AppendAsync(string chunk)
    {
        var now = Interlocked.Increment(ref _inFlight);
        int seen;
        while (now > (seen = Volatile.Read(ref _maxInFlight)))
            Interlocked.CompareExchange(ref _maxInFlight, now, seen);

        await Task.Yield();
        lock (_text)
            _text.Append(chunk);
        Interlocked.Decrement(ref _inFlight);
    }
}
