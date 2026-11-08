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

    /// <summary>Reads lines ended by LF, CR or CRLF, none longer than <paramref name="max"/>.</summary>
    private sealed class BoundedLineReader(TextReader reader, int max)
    {
        private readonly char[] _buffer = new char[4096];
        private readonly StringBuilder _line = new();
        private int _position;
        private int _length;
        private bool _skipLf;

        public async ValueTask<string?> ReadLineAsync(CancellationToken ct)
        {
            _line.Clear();
            while (true)
            {
                if (_position == _length)
                {
                    _length = await reader.ReadAsync(_buffer.AsMemory(), ct).ConfigureAwait(false);
                    _position = 0;
                    if (_length == 0)
                        return _line.Length > 0 ? _line.ToString() : null;
                }

                var c = _buffer[_position++];
                if (_skipLf)
                {
                    _skipLf = false;
                    if (c == '\n')
                        continue;
                }

                if (c == '\n')
                    return _line.ToString();
                if (c == '\r')
                {
                    _skipLf = true;
                    return _line.ToString();
                }

                if (_line.Length >= max)
                    throw new InvalidDataException($"An SSE line is longer than {max} characters: the stream is refused.");
                _line.Append(c);
            }
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
        string? id = null;
        var eventType = "";
        var data = new StringBuilder();
        var hasData = false;
        int? retry = null;

        while (true)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null)
                yield break;

            if (line.Length == 0)
            {
                if (hasData || id is not null || retry is not null)
                    yield return new SseEvent(id, eventType.Length == 0 ? "message" : eventType, data.ToString(), retry);
                id = null;
                eventType = "";
                data.Clear();
                hasData = false;
                retry = null;
                continue;
            }

            if (line[0] == ':')
                continue;

            var colon = line.IndexOf(':', StringComparison.Ordinal);
            var field = colon < 0 ? line : line[..colon];
            var value = colon < 0 ? "" : line[(colon + 1)..];
            if (value.StartsWith(' '))
                value = value[1..];

            switch (field)
            {
                case "data":
                    if (data.Length + value.Length + 1 > maxLineLength)
                        throw new InvalidDataException($"An SSE event is longer than {maxLineLength} characters: the stream is refused.");
                    if (hasData)
                        data.Append('\n');
                    data.Append(value);
                    hasData = true;
                    break;
                case "event":
                    eventType = value;
                    break;
                case "id":
                    id = value;
                    break;
                case "retry":
                    if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var ms))
                        retry = ms;
                    break;
            }
        }
    }
}
