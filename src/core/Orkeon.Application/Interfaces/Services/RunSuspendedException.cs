namespace Orkeon.Application.Interfaces.Services;

/// <summary>
/// Raised when a run stops on a question to a human it cannot wait for (FWK-23): the question is persisted and the run
/// ends as <c>waiting_for_input</c>. It is final: a retry never replays it and a checkpoint never records it as a failure.
/// </summary>
public sealed class RunSuspendedException : Exception
{
    /// <summary>Initializes a new instance.</summary>
    /// <param name="requestId">The question the run stopped on.</param>
    public RunSuspendedException(string requestId)
        : base($"The run is waiting for an answer to '{requestId}'.")
    {
        RequestId = requestId;
    }

    /// <summary>Initializes a new instance.</summary>
    public RunSuspendedException()
        : this("unknown")
    {
    }

    /// <summary>Initializes a new instance.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public RunSuspendedException(string message, Exception innerException)
        : base(message, innerException)
    {
        RequestId = "unknown";
    }

    /// <summary>Gets the question the run stopped on.</summary>
    public string RequestId { get; }
}
