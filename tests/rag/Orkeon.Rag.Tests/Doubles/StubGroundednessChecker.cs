using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Tests.Doubles;

/// <summary>
/// Hand-written scripted <see cref="IGroundednessChecker"/>: returns the
/// <see cref="ScriptedResults"/> first-in-first-out (then
/// <see cref="DefaultResult"/>) and records every call — drives the
/// groundedness re-loop of the corrective graph deterministically.
/// </summary>
public sealed class StubGroundednessChecker : IGroundednessChecker
{
    /// <summary>Results consumed first-in-first-out before falling back to <see cref="DefaultResult"/>.</summary>
    public Queue<GroundednessResult> ScriptedResults { get; } = new();

    /// <summary>Result returned once the script is exhausted.</summary>
    public GroundednessResult DefaultResult { get; set; } = new() { IsGrounded = true, Score = 1.0 };

    /// <summary>Calls received, in order.</summary>
    public List<(string Question, string Answer, IReadOnlyList<ScoredChunk> Context)> Calls { get; } = [];

    public Task<GroundednessResult> CheckAsync(
        string question,
        string answer,
        IReadOnlyList<ScoredChunk> context,
        CancellationToken cancellationToken = default)
    {
        Calls.Add((question, answer, context));
        var result = ScriptedResults.Count > 0 ? ScriptedResults.Dequeue() : DefaultResult;
        return Task.FromResult(result);
    }
}
