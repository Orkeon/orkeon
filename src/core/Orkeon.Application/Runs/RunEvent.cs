using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

namespace Orkeon.Application.Runs;

/// <summary>
/// One event of a run, before it is written anywhere: the envelope <c>{ v, seq, ts, kind, ...identity }</c> of the
/// run event protocol plus the flat payload. The wire line, the JSONL history file and the Sqlite row all carry
/// these same fields; <see cref="RunId"/> is the one that never reaches the wire, because the stream belongs to
/// one run and the history has to tell runs apart.
/// </summary>
[Experimental("ORKEXP007", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public sealed record RunEvent
{
    /// <summary>The run this event belongs to (the run identity of <c>IRunScope</c>); null for an event outside any run.</summary>
    public string? RunId { get; init; }

    /// <summary>Position in the run's stream, strictly increasing from 1; 0 until a publisher stamped the event.</summary>
    public int Seq { get; init; }

    /// <summary>When the event was emitted.</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>The event kind, e.g. <c>run.started</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>The crew execution this event belongs to, when known.</summary>
    public string? CrewId { get; init; }

    /// <summary>The agent that emitted it, when known.</summary>
    public string? AgentId { get; init; }

    /// <summary>Ties every event of one unit of work together; the run id unless the emitter named another.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>The event that caused this one, when known.</summary>
    public string? CausationId { get; init; }

    /// <summary>The kind-specific properties, flat; null when the kind carries none.</summary>
    public JsonObject? Payload { get; init; }
}

/// <summary>
/// The identity an event carries in its envelope: which crew, which agent, and how it chains to what caused it.
/// An absent field is omitted from the line, never written as null.
/// </summary>
[Experimental("ORKEXP007", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public sealed record RunEventScope
{
    /// <summary>Nothing known: the shape most events carry.</summary>
    public static RunEventScope None { get; } = new();

    /// <summary>The crew execution this event belongs to.</summary>
    public string? CrewId { get; init; }

    /// <summary>The agent that emitted it; absent for crew-level events.</summary>
    public string? AgentId { get; init; }

    /// <summary>Ties every event of one unit of work together.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>The event that caused this one.</summary>
    public string? CausationId { get; init; }
}
