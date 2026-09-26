using MimeKit;

namespace Orkeon.Tools.Email.Mailboxes;

/// <summary>Measures a message as it will travel, without buffering it.</summary>
internal static class MessageSizes
{
    /// <summary>The size in bytes of <paramref name="message"/> once written.</summary>
    public static long Measure(MimeMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        using var counter = new CountingStream();
        message.WriteTo(counter);
        return counter.Length;
    }

    /// <summary>A write-only sink that only counts.</summary>
    private sealed class CountingStream : Stream
    {
        private long _length;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _length;

        public override long Position
        {
            get => _length;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
            // Nothing is buffered.
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => _length += count;
    }
}
