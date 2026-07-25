using System.Collections.Immutable;

namespace Orkeon.Rag.Abstractions.Models;

/// <summary>
/// Result of an <see cref="Interfaces.IGroundednessChecker"/> pass: whether an
/// answer is anchored in the retrieved context.
/// </summary>
public sealed record GroundednessResult
{
    /// <summary>Whether the answer is considered grounded in the provided context.</summary>
    public required bool IsGrounded { get; init; }

    /// <summary>Groundedness confidence in [0, 1], higher is more grounded.</summary>
    public double Score { get; init; }

    /// <summary>Claims of the answer that could not be supported by the context.</summary>
    public ImmutableList<string> UnsupportedClaims { get; init; } = ImmutableList<string>.Empty;

    /// <summary>Optional checker rationale (kept in the trace for debugging/eval).</summary>
    public string? Rationale { get; init; }
}
