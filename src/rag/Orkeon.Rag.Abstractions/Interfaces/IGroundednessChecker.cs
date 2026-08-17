using Orkeon.Rag.Abstractions.Models;
using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Rag.Abstractions.Interfaces;

/// <summary>
/// Verifies that a generated answer is anchored in the retrieved context
/// (anti-hallucination gate, guide §8).
/// </summary>
[Experimental("ORKEXP003", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public interface IGroundednessChecker
{
    /// <summary>Checks whether <paramref name="answer"/> to <paramref name="question"/> is grounded in <paramref name="context"/>.</summary>
    Task<GroundednessResult> CheckAsync(
        string question,
        string answer,
        IReadOnlyList<ScoredChunk> context,
        CancellationToken cancellationToken = default);
}
