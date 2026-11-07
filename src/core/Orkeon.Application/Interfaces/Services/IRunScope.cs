namespace Orkeon.Application.Interfaces.Services;

/// <summary>
/// The identity of the run in flight, visible to every decorator and observer the run reaches.
/// </summary>
/// <param name="ExecutionId">The run's one identifier.</param>
/// <param name="CrewKey">The stable key of the crew.</param>
/// <param name="Resume">What the run resumes from, or null for a fresh run.</param>
public sealed record RunScope(ExecutionId ExecutionId, string CrewKey, ResumePlan? Resume = null);

/// <summary>
/// The work a resumed run does not redo: the outputs of the tasks its session had completed, by
/// checkpoint key (<c>"{TaskKey}#{n}"</c>), as the checkpointing decorator stored them.
/// </summary>
/// <param name="SessionId">The checkpoint session the run continues: its own execution id.</param>
/// <param name="CompletedOutputs">The stored output of every completed task, by checkpoint key.</param>
public sealed record ResumePlan(string SessionId, IReadOnlyDictionary<string, string?> CompletedOutputs)
{
    /// <summary>
    /// The ids of the tasks of this run that were answered from the checkpoint, filled by the
    /// checkpointing decorator as it answers them: what lets an event say a task was resumed, whatever
    /// the orchestration mode that reports it.
    /// </summary>
    public System.Collections.Concurrent.ConcurrentDictionary<string, byte> ResumedTaskIds { get; } = new(StringComparer.Ordinal);
}

/// <summary>
/// The ambient run identity: entered by the orchestrator for the length of a run, read by whatever
/// the run calls (event observers, checkpointing decorators). It flows with the asynchronous
/// control flow, so concurrent runs never see each other's.
/// </summary>
public interface IRunScope
{
    /// <summary>Gets the run in flight on this control flow, or null outside any run.</summary>
    RunScope? Current { get; }

    /// <summary>Makes <paramref name="scope"/> the current run until the returned token is disposed.</summary>
    IDisposable Enter(RunScope scope);
}
