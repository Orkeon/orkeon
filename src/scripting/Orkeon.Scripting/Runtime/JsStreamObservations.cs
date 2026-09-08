namespace Orkeon.Scripting.Runtime;

#pragma warning disable IDE1006 // JS-facing members are camelCase on purpose.

/// <summary>
/// Side-channel of one streamed call, readable from JS on the object
/// <c>stream()</c> returns: <c>usage</c> (null until the terminal event, and
/// null for good if the provider reported none) and <c>reasoningChunks</c>.
/// </summary>
/// <remarks>
/// A PLAIN CLR OBJECT, mutated from the stream loop and read through Jint's
/// interop when the script asks for it. That indirection is the whole point:
/// the first version of this called JS callbacks (<c>onReasoning</c> /
/// <c>onComplete</c>) from the loop, which re-enters the engine from whatever
/// thread the enumeration happens to be on. Jint's <c>Engine</c> is
/// single-threaded, and a real concurrent run proved what that costs: seven area
/// writers streaming concurrently, the first callback fired mid-flight, and
/// <c>ScriptFunction.Call</c> threw NullReferenceException inside the engine.
/// All seven writers fell back to a placeholder and the script died silently
/// after assembling the document — with six HTTP 200s in flight. Mutating CLR
/// state costs nothing and is read on the engine's own thread.
/// </remarks>
public sealed class StreamObservations
{
    /// <summary>Terminal usage; null while the stream runs, and null if none was reported.</summary>
    public StreamUsage? usage { get; internal set; }

    /// <summary>Reasoning deltas seen. 0 on a model that emits none.</summary>
    public int reasoningChunks { get; internal set; }
}

/// <summary>Terminal token counts of a streamed call. Null fields = the provider said nothing.</summary>
public sealed class StreamUsage
{
    /// <summary>Total tokens the provider attributed to the call.</summary>
    public int tokensUsed { get; internal set; }

    /// <summary>Prompt tokens; null when the provider reported none.</summary>
    public int? promptTokens { get; internal set; }

    /// <summary>Completion tokens (reasoning included, same budget as the answer); null when unreported.</summary>
    public int? completionTokens { get; internal set; }

    /// <summary>Prompt tokens served from the provider's cache; null when unreported.</summary>
    public int? cacheHitTokens { get; internal set; }

    /// <summary>Model the provider says answered; empty when it did not say.</summary>
    public string model { get; internal set; } = string.Empty;
}

