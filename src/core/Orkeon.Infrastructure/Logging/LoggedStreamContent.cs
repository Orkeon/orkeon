using System.Net;
using System.Text;

namespace Orkeon.Infrastructure.Logging;

/// <summary>
/// Hands a streamed response body to the caller as the bytes arrive, keeps a bounded copy of what
/// went through, and reports it once when the content is disposed.
/// </summary>
internal sealed class LoggedStreamContent : HttpContent
{
    private readonly HttpContent _inner;
    private readonly int _maxCaptureBytes;
    private readonly Action<string> _completed;
    private readonly MemoryStream _captured = new();
    private readonly object _gate = new();
    private int _reported;

    /// <param name="inner">The response content to pass through.</param>
    /// <param name="maxCaptureBytes">Bytes kept for the log; zero or negative keeps everything.</param>
    /// <param name="completed">Called once, on dispose, with the text that went through.</param>
    internal LoggedStreamContent(HttpContent inner, int maxCaptureBytes, Action<string> completed)
    {
        _inner = inner;
        _maxCaptureBytes = maxCaptureBytes;
        _completed = completed;
        foreach (var header in inner.Headers)
            Headers.TryAddWithoutValidation(header.Key, header.Value);
    }

    /// <inheritdoc />
    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        var source = await ReadAsStreamAsync().ConfigureAwait(false);
        await source.CopyToAsync(stream).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override bool TryComputeLength(out long length)
    {
        length = -1;
        return false;
    }

    /// <inheritdoc />
    protected override async Task<Stream> CreateContentReadStreamAsync()
    {
        var source = await _inner.ReadAsStreamAsync().ConfigureAwait(false);
        return new TeeStream(source, this);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
            if (Interlocked.Exchange(ref _reported, 1) == 0)
                _completed(Encoding.UTF8.GetString(_captured.GetBuffer(), 0, (int)_captured.Length));
        }

        base.Dispose(disposing);
    }

    private void Capture(ReadOnlySpan<byte> bytes)
    {
        lock (_gate)
        {
            if (_maxCaptureBytes > 0)
                bytes = bytes[..Math.Min(bytes.Length, Math.Max(0, _maxCaptureBytes - (int)_captured.Length))];

            _captured.Write(bytes);
        }
    }

    private sealed class TeeStream(Stream source, LoggedStreamContent owner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            owner.Capture(buffer.Span[..read]);
            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(Span<byte> buffer)
        {
            var read = source.Read(buffer);
            owner.Capture(buffer[..read]);
            return read;
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
