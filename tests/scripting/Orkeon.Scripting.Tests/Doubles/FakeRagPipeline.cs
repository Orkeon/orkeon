using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Scripting.Tests.Doubles;

/// <summary>
/// Hand-written double for <see cref="IRagPipeline"/>: returns <see cref="Answer"/>
/// and records the last <see cref="RagQuery"/> received.
/// </summary>
public sealed class FakeRagPipeline : IRagPipeline
{
    /// <summary>Answer returned by <see cref="QueryAsync"/>.</summary>
    public RagAnswer Answer { get; set; } = new() { Text = "fake answer" };

    /// <summary>Last query received.</summary>
    public RagQuery? LastQuery { get; private set; }

    /// <summary>Number of <see cref="QueryAsync"/> calls.</summary>
    public int CallCount { get; private set; }

    public Task<RagAnswer> QueryAsync(RagQuery query, CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastQuery = query;
        return Task.FromResult(Answer);
    }
}
