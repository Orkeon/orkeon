namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// A read-only stream that serves <paramref name="prefix"/> and then never ends: every further
/// read waits on its cancellation token. Served as a <see cref="System.Net.Http.StreamContent"/>
/// through <see cref="MockHttpMessageHandler.SetResponseFactory"/>, it is a provider that sent
/// its headers (and maybe its first chunks) and then went silent — what a model that thinks for
/// minutes, or a gateway that stopped forwarding, looks like from the client (LLM-12).
/// </summary>
public sealed class HangingStream(byte[] prefix) : Stream
{
    private int _position;

    /// <summary>How many reads waited on the token, i.e. how many times the reader asked past the prefix.</summary>
    public int HangingReads { get; private set; }

    /// <inheritdoc />
    public override bool CanRead => true;
    /// <inheritdoc />
    public override bool CanSeek => false;
    /// <inheritdoc />
    public override bool CanWrite => false;
    /// <inheritdoc />
    public override long Length => throw new NotSupportedException();
    /// <inheritdoc />
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    /// <inheritdoc />
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var remaining = prefix.Length - _position;
        if (remaining > 0)
        {
            var count = Math.Min(remaining, buffer.Length);
            prefix.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        HangingReads++;
        await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        return 0;
    }

    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("Read it asynchronously, as the providers do.");
    /// <inheritdoc />
    public override void Flush() { }
    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();
    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
