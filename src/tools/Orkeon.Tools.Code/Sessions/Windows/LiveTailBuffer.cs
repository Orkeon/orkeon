namespace Orkeon.Tools.Code.Sessions.Windows;

/// <summary>The last 64 KiB of a job's output, safe to read while the pumps append.</summary>
internal sealed class LiveTailBuffer
{
    public const int Capacity = 64 * 1024;
    private readonly Lock _lock = new();
    private byte[] _bytes = [];
    private bool _truncated;

    public void Append(ReadOnlySpan<byte> chunk)
    {
        lock (_lock)
        {
            var all = new byte[_bytes.Length + chunk.Length];
            _bytes.CopyTo(all, 0);
            chunk.CopyTo(all.AsSpan(_bytes.Length));
            if (all.Length > Capacity)
            {
                _truncated = true;
                _bytes = all[^Capacity..];
            }
            else
            {
                _bytes = all;
            }
        }
    }

    public ShellLiveTail Snapshot()
    {
        lock (_lock)
        {
            return new ShellLiveTail(System.Text.Encoding.UTF8.GetString(_bytes), _truncated);
        }
    }
}
