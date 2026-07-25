using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Scripting.Cli.Tests.Doubles;

/// <summary>
/// Hand-written double for <see cref="IRagPipeline"/>: returns <see cref="Answer"/>
/// and records the last <see cref="RagQuery"/> received. Pre-registered through
/// <c>RagCommandOptionsBase.ConfigureTestServices</c> so it wins the TryAdd race
/// over the real pipeline.
/// </summary>
internal sealed class FakeRagPipeline : IRagPipeline
{
    /// <summary>Answer returned by <see cref="QueryAsync"/>.</summary>
    public RagAnswer Answer { get; set; } = new() { Text = "fake answer" };

    /// <summary>Last query received.</summary>
    public RagQuery? LastQuery { get; private set; }

    public Task<RagAnswer> QueryAsync(RagQuery query, CancellationToken cancellationToken = default)
    {
        LastQuery = query;
        return Task.FromResult(Answer);
    }
}
