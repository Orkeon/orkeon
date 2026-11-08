namespace Orkeon.Scripting.Cli.Commands.Run;

/// <summary>
/// Wire spellings of the run event stream (BUS-02), re-exported from
/// <see cref="Orkeon.Constants.Protocol.RunEventKinds"/> so this verb keeps one name for them.
/// The Atelier's own kinds live in <c>ForgeEventKinds</c>; both verbs share the envelope, not
/// the vocabulary.
/// <para>
/// The declarations themselves live in the satellite because the process that READS this stream
/// - Orkeon Studio - cannot reference this project, and used to carry its own copy. The two
/// drifted: four kinds emitted here were unknown there, and an unknown kind is silently ignored
/// rather than reported.
/// </para>
/// </summary>
internal static class RunEventKinds
{
    /// <summary>Opening event: what is about to run.</summary>
    public const string RunStarted = Orkeon.Constants.Protocol.RunEventKinds.RunStarted;

    /// <summary>One task of the crew started - its agent's turn begins.</summary>
    public const string TaskStarted = Orkeon.Constants.Protocol.RunEventKinds.TaskStarted;

    /// <summary>One task of the crew completed - the granularity <c>ICrewExecutionHook</c> gives.</summary>
    public const string TaskCompleted = Orkeon.Constants.Protocol.RunEventKinds.TaskCompleted;

    /// <summary>The token meter moved.</summary>
    public const string CostUpdated = Orkeon.Constants.Protocol.RunEventKinds.CostUpdated;

    /// <summary>Token-by-token generation; only under <c>--stream</c>.</summary>
    public const string LlmDelta = Orkeon.Constants.Protocol.RunEventKinds.LlmDelta;

    /// <summary>See <see cref="Orkeon.Constants.Protocol.RunEventKinds.LlmCallStarted"/>.</summary>
    public const string LlmCallStarted = Orkeon.Constants.Protocol.RunEventKinds.LlmCallStarted;

    /// <summary>See <see cref="Orkeon.Constants.Protocol.RunEventKinds.LlmCallProgress"/>.</summary>
    public const string LlmCallProgress = Orkeon.Constants.Protocol.RunEventKinds.LlmCallProgress;

    /// <summary>See <see cref="Orkeon.Constants.Protocol.RunEventKinds.LlmCallFinished"/>.</summary>
    public const string LlmCallFinished = Orkeon.Constants.Protocol.RunEventKinds.LlmCallFinished;

    /// <summary>See <see cref="Orkeon.Constants.Protocol.RunEventKinds.LlmFallback"/>.</summary>
    public const string LlmFallback = Orkeon.Constants.Protocol.RunEventKinds.LlmFallback;

    /// <summary>Closing event; mirrors the process exit code.</summary>
    public const string RunFinished = Orkeon.Constants.Protocol.RunEventKinds.RunFinished;

    /// <summary>An anomaly, recoverable or not.</summary>
    public const string Error = Orkeon.Constants.Protocol.RunEventKinds.Error;

    /// <summary>A running crew is waiting on a human (BUS-04).</summary>
    public const string InputNeeded = Orkeon.Constants.Protocol.RunEventKinds.InputNeeded;

    /// <summary>Inbound: the human's answer, correlated to the question (stdin).</summary>
    public const string InputGiven = Orkeon.Constants.Protocol.RunEventKinds.InputGiven;

    /// <summary>A tool was invoked; correlated with its <see cref="ToolReturned"/>.</summary>
    public const string ToolCalled = Orkeon.Constants.Protocol.RunEventKinds.ToolCalled;

    /// <summary>A tool finished, successfully or not - including when it threw.</summary>
    public const string ToolReturned = Orkeon.Constants.Protocol.RunEventKinds.ToolReturned;

    /// <summary>
    /// One agent handed work to another. A delegation is a tool call underneath, but calling
    /// it one would bury the single thing that makes a hierarchical run readable.
    /// </summary>
    public const string DelegationStarted = Orkeon.Constants.Protocol.RunEventKinds.DelegationStarted;

    /// <summary>The team grew at runtime - the autonomous mode's most opaque moment.</summary>
    public const string AgentSpawned = Orkeon.Constants.Protocol.RunEventKinds.AgentSpawned;

    /// <summary>See <see cref="Orkeon.Constants.Protocol.RunEventKinds.GraphNodeEntered"/>.</summary>
    public const string GraphNodeEntered = Orkeon.Constants.Protocol.RunEventKinds.GraphNodeEntered;
}
