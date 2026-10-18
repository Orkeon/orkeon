using System.Net.Http.Headers;

namespace Orkeon.Tools.Drive.Drives;

/// <summary>
/// Builds request bodies from an upload. A request disposes its content, and its content the
/// stream under it: the upload's stream is wrapped so that it survives a request, rewound for
/// the one more try a refused token earns, and closed by its owner only.
/// </summary>
internal static class UploadContents
{
    /// <summary>The whole upload as one body, rewound first.</summary>
    public static HttpContent Whole(DriveUpload upload)
    {
        ArgumentNullException.ThrowIfNull(upload);
        if (upload.Content.CanSeek)
            upload.Content.Position = 0;
        var content = new StreamContent(new KeptOpenStream(upload.Content));
        content.Headers.ContentType = MediaTypeHeaderValue.TryParse(upload.MimeType, out var type) ? type : new MediaTypeHeaderValue("application/octet-stream");
        content.Headers.ContentLength = upload.Length;
        return content;
    }

    /// <summary>Reads the next chunk of at most <paramref name="size"/> bytes from the upload's current position.</summary>
    public static async Task<byte[]> ReadChunkAsync(DriveUpload upload, int size, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(upload);
        var buffer = new byte[size];
        var filled = 0;
        while (filled < size)
        {
            var read = await upload.Content.ReadAsync(buffer.AsMemory(filled, size - filled), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;
            filled += read;
        }

        return filled == size ? buffer : buffer[..filled];
    }

    /// <summary>A stream whose disposal leaves the stream under it open.</summary>
    private sealed class KeptOpenStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush()
        {
            // Read-only: nothing to flush.
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            inner.ReadAsync(buffer, offset, count, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
