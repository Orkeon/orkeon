using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Runs;
using Orkeon.Hosting.Runs;

namespace Orkeon.Scripting.Cli.Events;

/// <summary>
/// The stdout destination of <c>orkeon run --events jsonl</c>: one line of the run event protocol per event,
/// flushed at once. It writes what the publisher stamped and decides nothing: the line is the contract, pinned
/// byte for byte by a golden test.
/// </summary>
internal sealed class StdoutJsonLinesRunEventSink : IRunEventSink
{
    private readonly TextWriter _output;

    /// <summary>Writes to <paramref name="output"/> (the console's stdout).</summary>
    public StdoutJsonLinesRunEventSink(TextWriter output) =>
        _output = output ?? throw new ArgumentNullException(nameof(output));

    /// <inheritdoc />
    /// <remarks>The publisher writes one event at a time, so lines never interleave.</remarks>
    public async System.Threading.Tasks.Task WriteAsync(RunEvent runEvent, CancellationToken ct = default)
    {
        await _output.WriteLineAsync(RunEventWire.Serialize(runEvent).AsMemory(), ct).ConfigureAwait(false);
        await _output.FlushAsync(ct).ConfigureAwait(false);
    }
}

/// <summary>Builds the publisher a run writes its events to.</summary>
internal static class RunEventStreams
{
    /// <summary>The publisher of a run: the stdout stream when <paramref name="toStdout"/>, no destination otherwise; the history attaches to it later.</summary>
    public static RunEventPublisher Create(TextWriter output, IRunScope? runScope, bool toStdout, TimeProvider? time = null) =>
        new(toStdout ? [new StdoutJsonLinesRunEventSink(output)] : [], time ?? TimeProvider.System, runScope);

    /// <summary>A publisher whose only destination is the stdout stream; further destinations attach to it.</summary>
    public static RunEventPublisher Stdout(TextWriter output, IRunScope? runScope = null, TimeProvider? time = null) =>
        new([new StdoutJsonLinesRunEventSink(output)], time ?? TimeProvider.System, runScope);
}
