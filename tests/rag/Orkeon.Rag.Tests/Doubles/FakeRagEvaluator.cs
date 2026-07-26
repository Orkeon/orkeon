using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Tests.Doubles;

/// <summary>
/// Hand-written double for <see cref="IRagEvaluator"/>: builds a minimal report
/// echoing the dataset/options received and records every call.
/// </summary>
public sealed class FakeRagEvaluator : IRagEvaluator
{
    /// <summary>Options received, in order.</summary>
    public List<RagEvalOptions> Calls { get; } = [];

    /// <summary>Aggregate stamped on every returned report.</summary>
    public RagEvalAggregate Aggregate { get; set; } = new() { CaseCount = 0 };

    public Task<RagEvalReport> RunAsync(
        RagEvalDataset dataset, RagEvalOptions options, CancellationToken cancellationToken = default)
    {
        Calls.Add(options);
        return Task.FromResult(new RagEvalReport
        {
            DatasetName = dataset.Name,
            Profile = options.Profile,
            Collection = options.Collection,
            K = options.K,
            Judge = RagJudgeMode.Heuristic,
            Aggregate = Aggregate with { CaseCount = dataset.Cases.Count },
        });
    }
}
