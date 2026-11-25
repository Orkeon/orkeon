namespace Orkeon.Host.Gateway;

/// <summary>The content of a response, which it owns, given up when nothing comes for a while.</summary>
/// <param name="response">The response, disposed with the stream.</param>
/// <param name="content">Its content.</param>
/// <param name="idle">How long it may send nothing.</param>
/// <param name="server">How an error names who sends it: no address.</param>
internal sealed class IdleBoundedStream(HttpResponseMessage response, Stream content, TimeSpan idle, string server) : Stream
{
    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bound.CancelAfter(idle);
        try
        {
            return await content.ReadAsync(buffer, bound.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IOException($"{server} stopped sending the file.");
        }
        catch (HttpRequestException ex)
        {
            throw new IOException($"{server} could not be read ({ex.HttpRequestError}).");
        }
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override void Flush() => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            content.Dispose();
            response.Dispose();
        }

        base.Dispose(disposing);
    }
}
