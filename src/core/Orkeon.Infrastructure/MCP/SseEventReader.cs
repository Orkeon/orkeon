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
/// </summary>
internal static class SseEventReader
{
    public static async IAsyncEnumerable<SseEvent> ReadAsync(
        Stream stream, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
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
                if (hasData)
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
