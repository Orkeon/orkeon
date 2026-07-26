using System.Collections.Immutable;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Evaluation;

namespace Orkeon.Scripting.Cli.Tests.Doubles;

/// <summary>
/// Hand-written double for <see cref="IRagEvalHarness"/>: returns
/// <see cref="Result"/> (or a minimal default derived from the request) and
/// records every request. Pre-registered through
/// <c>RagCommandOptionsBase.ConfigureTestServices</c> so it wins the TryAdd race
/// over the real harness.
/// </summary>
internal sealed class FakeRagEvalHarness : IRagEvalHarness
{
    /// <summary>Result returned by <see cref="RunAsync"/>; <c>null</c> builds a minimal default.</summary>
    public RagEvalRunResult? Result { get; set; }

    /// <summary>Per-case results stamped on the default report (drives the gate tests).</summary>
    public ImmutableList<RagEvalCaseResult> DefaultCases { get; set; } = ImmutableList<RagEvalCaseResult>.Empty;

    /// <summary>Last request received.</summary>
    public RagEvalRunRequest? LastRequest { get; private set; }

    public Task<RagEvalRunResult> RunAsync(
        RagEvalRunRequest request, CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        return Task.FromResult(Result ?? BuildDefault(request));
    }

    private RagEvalRunResult BuildDefault(RagEvalRunRequest request)
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
            Cases = DefaultCases,
            Aggregate = new RagEvalAggregate
            {
                CaseCount = DefaultCases.Count,
                RecallAtK = 0.9,
                Mrr = 0.8,
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
