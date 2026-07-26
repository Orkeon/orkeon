using Microsoft.Extensions.DependencyInjection;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Evaluation;
using Orkeon.Scripting.Cli.Commands;
using Orkeon.Scripting.Cli.Tests.Doubles;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// End-to-end coverage for the <c>orkeon rag eval</c> verb driven in-process
/// (RAG-04/C1). The full host bootstrap and VFS mounts run for real; the
/// evaluation harness is a hand-written double pre-registered through the
/// internal <c>ConfigureTestServices</c> seam (it wins the TryAdd race in
/// <c>AddOrkeonRag</c>), so everything stays offline and deterministic.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class RagEvalCommandTests
{
    private static RagEvalCaseResult CaseResult(string id, double recall, double rr, params string[] tags) => new()
    {
        CaseId = id,
        Tags = [.. tags],
        RecallAtK = recall,
        ReciprocalRank = rr,
        Judge = RagJudgeMode.Heuristic,
    };

    [Fact]
    public async Task Eval_MapsTheDatasetPathToTheVfs_AndPrintsTheSummary()
    {
        using var scratch = new ScriptScratch();
        scratch.WriteFile("golden.yaml", "unused by the fake harness");
        var harness = new FakeRagEvalHarness();
        using var console = new TestConsole();

        var exit = await RagCommand.ExecuteEvalAsync(new RagEvalCommandOptions
        {
            Dataset = "./golden.yaml",
            WorkingDirectoryOverride = scratch.Root,
            ConfigureTestServices = (_, s) => s.AddSingleton<IRagEvalHarness>(harness),
        });

        Assert.Equal(Program.ExitOk, exit);
        Assert.Equal("/workspace/golden.yaml", harness.LastRequest?.DatasetPath);
        Assert.Empty(harness.LastRequest!.Profiles);
        Assert.True(harness.LastRequest.IngestCorpus);
        Assert.Contains("RAG evaluation — dataset 'golden'", console.Stdout, StringComparison.Ordinal);
        Assert.Contains("judge: heuristic", console.Stdout, StringComparison.Ordinal);
        Assert.Contains("/output/rag/eval/golden-default.md", console.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Eval_Compare_PassesTheProfileList_AndPrintsTheComparisonTable()
    {
        using var scratch = new ScriptScratch();
        var harness = new FakeRagEvalHarness();
        using var console = new TestConsole();

        var exit = await RagCommand.ExecuteEvalAsync(new RagEvalCommandOptions
        {
            Dataset = "golden.yaml",
            Compare = "fast, balanced,quality",
            Collection = "kb",
            K = 3,
            NoIngest = true,
            Reindex = true,
            WorkingDirectoryOverride = scratch.Root,
            ConfigureTestServices = (_, s) => s.AddSingleton<IRagEvalHarness>(harness),
        });

        Assert.Equal(Program.ExitOk, exit);
        Assert.Equal(["fast", "balanced", "quality"], harness.LastRequest?.Profiles);
        Assert.Equal("kb", harness.LastRequest?.Collection);
        Assert.Equal(3, harness.LastRequest?.K);
        Assert.False(harness.LastRequest!.IngestCorpus);
        Assert.True(harness.LastRequest.ReindexCorpus);
        Assert.Contains("| profile | recall@3 | MRR |", console.Stdout, StringComparison.Ordinal);
        Assert.Contains("| balanced |", console.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Eval_MinRecallGate_ExcludesCorrectifCases_AndFailsOnRegression()
    {
        using var scratch = new ScriptScratch();
        // Regular cases average recall 0.5 — the seeded correctif case (recall 0)
        // must NOT drag the gate down, so the gate value is 0.5, not 0.33.
        var harness = new FakeRagEvalHarness
        {
            DefaultCases =
            [
                CaseResult("q-1", 1.0, 1.0),
                CaseResult("q-2", 0.0, 0.0),
                CaseResult("q-7", 0.0, 0.0, RagEvalCase.CorrectiveTag),
            ],
        };

        // Threshold below the gated value → pass.
        using (var console = new TestConsole())
        {
            var exit = await RagCommand.ExecuteEvalAsync(new RagEvalCommandOptions
            {
                Dataset = "golden.yaml",
                MinRecall = 0.45,
                WorkingDirectoryOverride = scratch.Root,
                ConfigureTestServices = (_, s) => s.AddSingleton<IRagEvalHarness>(harness),
            });
            Assert.Equal(Program.ExitOk, exit);
        }

        // Threshold above the gated value → regression, exit 1, actionable stderr.
        using (var console = new TestConsole())
        {
            var exit = await RagCommand.ExecuteEvalAsync(new RagEvalCommandOptions
            {
                Dataset = "golden.yaml",
                MinRecall = 0.75,
                WorkingDirectoryOverride = scratch.Root,
                ConfigureTestServices = (_, s) => s.AddSingleton<IRagEvalHarness>(harness),
            });
            Assert.Equal(Program.ExitScriptError, exit);
            Assert.Contains("REGRESSION", console.Stderr, StringComparison.Ordinal);
            Assert.Contains("recall@5 = 0.500 < threshold 0.750", console.Stderr, StringComparison.Ordinal);
            Assert.Contains("correctif", console.Stderr, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Eval_MinMrrGate_FailsOnRegression()
    {
        using var scratch = new ScriptScratch();
        var harness = new FakeRagEvalHarness
        {
            DefaultCases = [CaseResult("q-1", 1.0, 0.5)],
        };
        using var console = new TestConsole();

        var exit = await RagCommand.ExecuteEvalAsync(new RagEvalCommandOptions
        {
            Dataset = "golden.yaml",
            MinMrr = 0.9,
            WorkingDirectoryOverride = scratch.Root,
            ConfigureTestServices = (_, s) => s.AddSingleton<IRagEvalHarness>(harness),
        });

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("MRR = 0.500 < threshold 0.900", console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Eval_SurfacesHarnessFailures_AsScriptErrors()
    {
        using var scratch = new ScriptScratch();
        using var console = new TestConsole();

        // No fake harness: the real one runs and the dataset file does not exist.
        var exit = await RagCommand.ExecuteEvalAsync(new RagEvalCommandOptions
        {
            Dataset = "absent.yaml",
            WorkingDirectoryOverride = scratch.Root,
        });

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("absent.yaml", console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dispatch_KnowsTheEvalVerb()
    {
        using var console = new TestConsole();

        // Missing required --dataset → parse error, but the verb itself is known.
        var exit = await RagCommand.DispatchAsync(["eval"]);

        Assert.Equal(Program.ExitScriptError, exit);
    }
}
