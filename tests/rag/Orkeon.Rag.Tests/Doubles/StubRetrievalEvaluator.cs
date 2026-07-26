using Orkeon.Rag.Abstractions;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Tests.Doubles;

/// <summary>
/// Hand-written scripted <see cref="IRetrievalEvaluator"/>: returns the
/// <see cref="ScriptedVerdicts"/> first-in-first-out (then
/// <see cref="DefaultVerdict"/>) and records every call — drives the
/// conditional edges of the corrective graph deterministically.
/// </summary>
public sealed class StubRetrievalEvaluator : IRetrievalEvaluator
{
    /// <summary>Verdicts consumed first-in-first-out before falling back to <see cref="DefaultVerdict"/>.</summary>
    public Queue<RetrievalVerdict> ScriptedVerdicts { get; } = new();

    /// <summary>Verdict returned once the script is exhausted.</summary>
    public RetrievalVerdict DefaultVerdict { get; set; } = new() { Grade = RetrievalGrade.Correct };

    /// <summary>Calls received, in order.</summary>
    public List<(string Query, IReadOnlyList<ScoredChunk> Chunks)> Calls { get; } = [];

    public Task<RetrievalVerdict> EvaluateAsync(
        string query,
        IReadOnlyList<ScoredChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        Calls.Add((query, chunks));
        var verdict = ScriptedVerdicts.Count > 0 ? ScriptedVerdicts.Dequeue() : DefaultVerdict;
        return Task.FromResult(verdict);
    }
}
