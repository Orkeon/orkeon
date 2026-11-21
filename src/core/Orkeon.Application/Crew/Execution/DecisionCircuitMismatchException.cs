namespace Orkeon.Application.Crew.Execution;

/// <summary>
/// A crew with <c>decision.mode: optional</c> refused on resume because the decision circuit is not the one its
/// recorded decisions were taken with (#430): they came from the decision model and the circuit is now off, or
/// from their <c>without:</c> relay and the circuit is now on. The run fails with the
/// <c>decision_circuit_mismatch</c> error code, and <c>orkeon run</c> exits 1: the operator resumes it under the
/// circuit it was started with, or starts a new run.
/// </summary>
#pragma warning disable S3925
public sealed class DecisionCircuitMismatchException : InvalidOperationException
#pragma warning restore S3925
{
    /// <summary>Initializes a refusal with no crew named.</summary>
    public DecisionCircuitMismatchException()
        : base("The crew (decision.mode: optional) is resumed under another decision circuit than the one it was started with.")
    {
    }

    /// <summary>Initializes a refusal with its message.</summary>
    /// <param name="message">The message, naming the crew and both circuits.</param>
    public DecisionCircuitMismatchException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a refusal with its message and inner exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The inner exception.</param>
    public DecisionCircuitMismatchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
