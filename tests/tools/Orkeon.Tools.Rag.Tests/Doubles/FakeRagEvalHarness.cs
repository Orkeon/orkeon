using System.Collections.Immutable;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Evaluation;

namespace Orkeon.Tools.Rag.Tests.Doubles;

/// <summary>
/// Hand-written double for <see cref="IRagEvalHarness"/>: returns
/// <see cref="Result"/> (or a minimal default derived from the request) and
/// records every request received.
/// </summary>
public sealed class FakeRagEvalHarness : IRagEvalHarness
{
    /// <summary>Result returned by <see cref="RunAsync"/>; <c>null</c> builds a minimal one.</summary>
    public RagEvalRunResult? Result { get; set; }

    /// <summary>Requests received, in order.</summary>
    public List<RagEvalRunRequest> Requests { get; } = [];

    /// <summary>Optional exception thrown instead of returning.</summary>
    public Exception? ThrowOnRun { get; set; }

    public Task<RagEvalRunResult> RunAsync(
        RagEvalRunRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        if (ThrowOnRun is not null)
            throw ThrowOnRun;

        return Task.FromResult(Result ?? BuildDefault(request));
    }

    /// <summary>Builds a plausible result echoing the request (one report per requested profile).</summary>
    public static RagEvalRunResult BuildDefault(RagEvalRunRequest request)
    {
        var dataset = new RagEvalDataset
        {
            Name = "golden",
            Cases = [new RagEvalCase { Id = "q-1", Question = "q?" }],
        };
        var profiles = request.Profiles.Count > 0 ? request.Profiles : [RagEvalOptions.DefaultProfile];
        var reports = profiles.Select(profile => new RagEvalReport
        {
            DatasetName = dataset.Name,
            Profile = profile,
            Collection = request.Collection ?? "rag-eval-golden",
            K = request.K,
            Judge = RagJudgeMode.Heuristic,
            Aggregate = new RagEvalAggregate
            {
                CaseCount = 1,
                RecallAtK = 0.9,
                Mrr = 0.8,
                Groundedness = 0.7,
                AnswerRelevance = 0.6,
            },
        }).ToImmutableList();

        return new RagEvalRunResult
        {
            Dataset = dataset,
            Collection = request.Collection ?? "rag-eval-golden",
            Reports = reports,
            WrittenFiles = [.. reports.SelectMany(r =>
                new[] { $"/output/rag/eval/golden-{r.Profile}.md", $"/output/rag/eval/golden-{r.Profile}.json" })],
            ComparisonFile = reports.Count > 1 ? "/output/rag/eval/golden-compare.md" : null,
        };
    }
}
