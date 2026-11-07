using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Application.Runs;

/// <summary>
/// A destination of run events: the stdout JSONL stream of <c>orkeon run --events</c>, the durable history files,
/// the Sqlite history. The observers of a run write to one sink and never know how many destinations stand behind it.
/// </summary>
[Experimental("ORKEXP007", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public interface IRunEventSink
{
    /// <summary>Writes one event. A sink never throws for a failure of its own destination: observing a run must not change its outcome.</summary>
    /// <param name="runEvent">The event, stamped with its <see cref="RunEvent.Seq"/> and <see cref="RunEvent.Timestamp"/> by the publisher.</param>
    /// <param name="ct">Cancellation.</param>
    System.Threading.Tasks.Task WriteAsync(RunEvent runEvent, CancellationToken ct = default);
}
