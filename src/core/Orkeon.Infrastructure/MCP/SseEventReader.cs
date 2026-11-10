using System.Globalization;
using System.Text;

namespace Orkeon.Infrastructure.MCP;

/// <summary>One server-sent event: its <c>id</c>, <c>event</c> type, joined <c>data</c> and <c>retry</c> hint.</summary>
internal readonly record struct SseEvent(string? Id, string Event, string Data, int? RetryMilliseconds);

/// <summary>
/// Reads a <c>text/event-stream</c> body event by event, as the bytes arrive: an event is handed out at its
/// blank line, without waiting for the stream to end. Several <c>data:</c> lines of one event are joined with
/// a newline; comments and unknown fields are skipped. An event the stream ends in the middle of (no blank
/// line after it) is discarded, as the SSE specification says: a cut connection leaves truncated data.
/// An event with an <c>id</c> or a <c>retry</c> but no <c>data</c> (the priming event of the 2025-11-25
/// specification) is handed out with an empty <see cref="SseEvent.Data"/>: the caller keeps the id and the
/// hint and dispatches nothing. A line, or the joined <c>data</c> of one event, longer than
/// <see cref="MaxLineLength"/> characters ends the read with an <see cref="InvalidDataException"/>: a server cannot
/// make the client buffer without end, whether in one line or in many <c>data:</c> lines with no blank line.
/// </summary>
internal static class SseEventReader
{
    /// <summary>The longest line, in characters, the reader accepts.</summary>
    internal const int MaxLineLength = 16 * 1024 * 1024;

    /// <summary>Reads lines ended by LF, CR or CRLF, none longer than the bound.</summary>
    private sealed class BoundedLineReader
    {
        private readonly TextReader _reader;
        private readonly int _max;
        private readonly char[] _buffer;
        private readonly StringBuilder _line;
        private int _position;
        private int _length;
        private bool _skipLf;

        public BoundedLineReader(TextReader reader, int max)
        {
            _reader = reader;
            _max = max;
            _buffer = new char[4096];
            _line = new StringBuilder();
        }

        public async ValueTask<string?> ReadLineAsync(CancellationToken ct)
        {
            _line.Clear();
            while (true)
            {
                if (_position == _length && !await FillAsync(ct).ConfigureAwait(false))
                    return _line.Length > 0 ? _line.ToString() : null;

                var c = _buffer[_position++];
                if (SkipsPendingLf(c))
                    continue;

                if (c == '\n')
                    return _line.ToString();
                if (c == '\r')
                {
                    _skipLf = true;
                    return _line.ToString();
                }

                if (_line.Length >= _max)
                    throw new InvalidDataException($"An SSE line is longer than {_max} characters: the stream is refused.");
                _line.Append(c);
            }
        }

        /// <summary>Refills the buffer; false at the end of the stream.</summary>
        private async ValueTask<bool> FillAsync(CancellationToken ct)
        {
            _length = await _reader.ReadAsync(_buffer.AsMemory(), ct).ConfigureAwait(false);
            _position = 0;
            return _length != 0;
        }

        /// <summary>True when <paramref name="c"/> is the LF of a CRLF whose CR ended the previous line.</summary>
        private bool SkipsPendingLf(char c)
        {
            if (!_skipLf)
                return false;
            _skipLf = false;
            return c == '\n';
        }
    }

    /// <summary>The fields of the event being read.</summary>
    private sealed class EventBuilder(int maxLength)
    {
        private readonly StringBuilder _data = new();
        private string? _id;
        private string _eventType = "";
        private bool _hasData;
        private int? _retry;

        public bool IsReady => _hasData || _id is not null || _retry is not null;

        public SseEvent Build() =>
            new(_id, _eventType.Length == 0 ? "message" : _eventType, _data.ToString(), _retry);

        public void Reset()
        {
            _id = null;
            _eventType = "";
            _data.Clear();
            _hasData = false;
            _retry = null;
        }

        /// <summary>Applies one non-empty line: a comment is skipped, a field is recorded.</summary>
        public void Apply(string line)
        {
            if (line[0] == ':')
                return;

            var colon = line.IndexOf(':', StringComparison.Ordinal);
            var field = colon < 0 ? line : line[..colon];
            var value = colon < 0 ? "" : line[(colon + 1)..];
            if (value.StartsWith(' '))
                value = value[1..];

            switch (field)
            {
                case "data":
                    AppendData(value);
                    break;
                case "event":
                    _eventType = value;
                    break;
                case "id":
                    _id = value;
                    break;
                case "retry":
                    if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var ms))
                        _retry = ms;
                    break;
            }
        }

        private void AppendData(string value)
        {
            if (_data.Length + value.Length + 1 > maxLength)
                throw new InvalidDataException($"An SSE event is longer than {maxLength} characters: the stream is refused.");
            if (_hasData)
                _data.Append('\n');
            _data.Append(value);
            _hasData = true;
        }
    }

    public static IAsyncEnumerable<SseEvent> ReadAsync(Stream stream, CancellationToken ct = default) =>
        ReadAsync(stream, MaxLineLength, ct);

    internal static async IAsyncEnumerable<SseEvent> ReadAsync(
        Stream stream, int maxLineLength, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var text = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var reader = new BoundedLineReader(text, maxLineLength);
        var pending = new EventBuilder(maxLineLength);

        while (true)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null)
                yield break;

            if (line.Length > 0)
            {
                pending.Apply(line);
                continue;
            }

            if (pending.IsReady)
                yield return pending.Build();
            pending.Reset();
        }
    }
}
