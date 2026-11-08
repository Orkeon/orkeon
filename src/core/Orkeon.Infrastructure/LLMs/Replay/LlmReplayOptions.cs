namespace Orkeon.Infrastructure.LLMs.Replay;

/// <summary>What a run does with the calls it makes to a model (<c>Llm:Replay:Mode</c>).</summary>
public enum LlmReplayMode
{
    /// <summary>Nothing is recorded and nothing is replayed.</summary>
    Off,

    /// <summary>Every call goes to the model and its answer is written to the cassette.</summary>
    Record,

    /// <summary>Every call is answered from the cassette; none reaches a model.</summary>
    Replay,
}

/// <summary>
/// The record-and-replay settings of the LLM providers (<c>Llm:Replay</c>, <c>--llm-record</c>,
/// <c>--llm-replay</c>).
/// </summary>
public sealed class LlmReplayOptions
{
    /// <summary>Whether calls are recorded, replayed or neither.</summary>
    public LlmReplayMode Mode { get; set; }

    /// <summary>The virtual path of the cassette, a JSONL file read or written through the file system.</summary>
    public string? Cassette { get; set; }
}
