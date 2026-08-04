using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Scripting.Tests.Doubles;

/// <summary>
/// Hand-written double for a pipeline that implements BOTH
/// <see cref="IRagPipeline"/> and <see cref="IRagRetrievalCapable"/>, counting
/// each surface separately so a test can prove which one <c>rag.retrieve</c>
/// actually took.
/// </summary>
/// <remarks>
/// <see cref="FakeRagPipeline"/> is deliberately left NON-capable: it is what the
/// "retrieve on a pipeline that cannot" case needs, and keeping the two doubles
/// apart means neither test can pass for the wrong reason.
/// </remarks>
public sealed class FakeRetrievalCapableRagPipeline : IRagPipeline, IRagRetrievalCapable
{
    /// <summary>Answer returned by <see cref="QueryAsync"/>.</summary>
    public RagAnswer Answer { get; set; } = new() { Text = "generated answer" };

    /// <summary>Answer returned by <see cref="RetrieveAsync"/>; text empty, as the real one.</summary>
    public RagAnswer Retrieved { get; set; } = new() { Text = "" };

    /// <summary>Last query received on either surface.</summary>
    public RagQuery? LastQuery { get; private set; }

    /// <summary>Number of <see cref="QueryAsync"/> calls.</summary>
    public int QueryCallCount { get; private set; }

    /// <summary>Number of <see cref="RetrieveAsync"/> calls.</summary>
    public int RetrieveCallCount { get; private set; }

    public Task<RagAnswer> QueryAsync(RagQuery query, CancellationToken cancellationToken = default)
    {
        QueryCallCount++;
        LastQuery = query;
        return Task.FromResult(Answer);
    }

    public Task<RagAnswer> RetrieveAsync(RagQuery query, CancellationToken cancellationToken = default)
    {
        RetrieveCallCount++;
        LastQuery = query;
        return Task.FromResult(Retrieved);
    }
}
