using System.Threading.Channels;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// A <see cref="TextReader"/> a test feeds line by line, as a client feeds a server's stdin: a read
/// waits for the next line, and returns <see langword="null"/> once <see cref="Complete"/> is called.
/// </summary>
public sealed class FakeLineReader : TextReader
{
    private readonly Channel<string> _lines = Channel.CreateUnbounded<string>();

    /// <summary>Queues one line for the reader.</summary>
    public void Send(string line) => _lines.Writer.TryWrite(line);

    /// <summary>How many lines were sent and not yet taken by a read: 0 means the server has read them all.</summary>
    public int Pending => _lines.Reader.Count;

    /// <summary>Ends the input: the client closed stdin.</summary>
    public void Complete() => _lines.Writer.TryComplete();

    /// <inheritdoc />
    public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _lines.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public override Task<string?> ReadLineAsync() => ReadLineAsync(CancellationToken.None).AsTask();
}
