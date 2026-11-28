using System.Text;

namespace Orkeon.Tools.Code.Sessions.Windows;

/// <summary>
/// One stream a Windows session reads from a command: its first <c>limit</c> characters, its last ones
/// once the head is full, and the count of the characters dropped between them, so the true length of
/// the stream survives however much it printed. Thread-safe.
/// </summary>
internal sealed class CapturedOutput
{
    private readonly int _limit;
    private readonly StringBuilder _head = new();
    private readonly StringBuilder _tail = new();
    private readonly Lock _sync = new();
    private long _omitted;

    /// <summary>Creates a capture keeping at most <paramref name="limit"/> characters at each end.</summary>
    public CapturedOutput(int limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        _limit = limit;
    }

    /// <summary>Appends <paramref name="count"/> characters of <paramref name="chars"/>.</summary>
    public void Append(char[] chars, int count)
    {
        ArgumentNullException.ThrowIfNull(chars);
        lock (_sync)
        {
            var toHead = Math.Min(Math.Max(0, _limit - _head.Length), count);
            _head.Append(chars, 0, toHead);
            if (toHead == count)
                return;

            _tail.Append(chars, toHead, count - toHead);

            // The tail is trimmed once it holds twice what it keeps, so a long stream costs one copy now and then.
            if (_tail.Length > 2 * _limit)
            {
                var drop = _tail.Length - _limit;
                _tail.Remove(0, drop);
                _omitted += drop;
            }
        }
    }

    /// <summary>The head, the tail (empty while the head is not full) and the characters dropped between them.</summary>
    public (string Head, string Tail, long Omitted) Snapshot()
    {
        lock (_sync)
        {
            var tailText = _tail.ToString();
            if (tailText.Length <= _limit)
                return (_head.ToString(), tailText, _omitted);
            var extra = tailText.Length - _limit;
            return (_head.ToString(), tailText[extra..], _omitted + extra);
        }
    }
}
