namespace Orkeon.Application.Interfaces.Services;

/// <summary>
/// The identity of the run in flight, visible to every decorator and observer the run reaches.
/// </summary>
/// <param name="ExecutionId">The run's one identifier.</param>
/// <param name="CrewKey">The stable key of the crew.</param>
/// <param name="Resume">The session this run resumes, or null for a fresh run.</param>
public sealed record RunScope(ExecutionId ExecutionId, string CrewKey, string? Resume = null);

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
