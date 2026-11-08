namespace Orkeon.Application.Interfaces.Services;

/// <summary>
/// What a caller decides about one run before it starts, for the kickoff that takes options.
/// </summary>
public sealed record KickoffOptions
{
    /// <summary>
    /// Gets the identifier the run takes: it is the checkpoint session, the correlation id of the
    /// run's events and the id of its A2A and API task. Null: the run mints one.
    /// </summary>
    public ExecutionId? ExecutionId { get; init; }

    /// <summary>
    /// Gets the stable key of the crew (its name and the virtual path of its file), the key a
    /// checkpoint survives a new process under. Null: the name of the crew.
    /// </summary>
    public string? CrewKey { get; init; }

    /// <summary>Gets the identifier of the session this run resumes, or null for a fresh run.</summary>
    public string? Resume { get; init; }

    /// <summary>
    /// Gets the answer to the question the resumed run stopped on (FWK-23), given with <see cref="Resume"/>: recorded before
    /// anything runs, so the task that asked finds it when it is replayed. Null: no answer.
    /// </summary>
    public string? ResumeInput { get; init; }
}
