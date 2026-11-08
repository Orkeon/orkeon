using System.Text.Json;
using Orkeon.Scripting.Cli.Commands;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// FWK-09 - <c>orkeon eval run | bench | compare</c> driven in-process over the real runner host: the
/// default suite, the dataset read through the VFS, the JSON reports, the exit codes.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class EvalCommandTests
{
    private const string Dataset = """
        [
          { "task_description": "Say the colour", "expected_output": "blue sky", "output": "blue sky" },
          { "task_description": "Say the animal", "expected_output": "black cat", "output": "black cat" }
        ]
        """;

    private static EvalRunOptions Run(ScriptScratch scratch, string datasetFile, Action<EvalRunOptions>? tune = null)
    {
        var options = new EvalRunOptions
        {
            Dataset = datasetFile,
            SettingsPath = scratch.WriteFile("settings.json", "{}"),
            Evaluators = "similarity",
            WorkingDirectoryOverride = scratch.Root,
        };
        tune?.Invoke(options);
        return options;
    }

    private static JsonDocument Report(ScriptScratch scratch, string name) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(scratch.Root, ".orkeon", "eval", name)));

    [Fact]
    [Trait("BR", "FWK-09")]
    public async Task ShouldWriteAJsonReportAndExitOk_WhenTheDatasetIsScored()
    {
        using var scratch = new ScriptScratch();
        var dataset = scratch.WriteFile("golden.json", Dataset);
        using var console = new TestConsole();

        var exit = await EvalCommand.ExecuteRunAsync(console, Run(scratch, dataset));

        Assert.True(exit == Program.ExitOk, console.Stderr);
        using var report = Report(scratch, "golden-eval.json");
        Assert.Equal("Default", report.RootElement.GetProperty("suite_name").GetString());
        Assert.Equal(2, report.RootElement.GetProperty("summary").GetProperty("total_cases").GetInt32());
        Assert.Equal("blue sky", report.RootElement.GetProperty("cases")[0].GetProperty("output").GetString());
        Assert.Contains("Similarity", console.Stdout, StringComparison.Ordinal);
        Assert.Contains("/output/eval/golden-eval.json", console.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-09")]
    public async Task ShouldExitOne_WhenTheOverallScoreIsUnderMinScore()
    {
        using var scratch = new ScriptScratch();
        var dataset = scratch.WriteFile("bad.json", """[ { "expected_output": "alpha beta gamma", "output": "zzz qqq" } ]""");
        using var console = new TestConsole();

        var exit = await EvalCommand.ExecuteRunAsync(console, Run(scratch, dataset, o => o.MinScore = 0.99));

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("BELOW THRESHOLD", console.Stderr, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(scratch.Root, ".orkeon", "eval", "bad-eval.json")), "the report is written before the gate");
    }

    [Fact]
    [Trait("BR", "FWK-09")]
    public async Task ShouldRefuseAnUnknownEvaluator_BeforeAnyWork()
    {
        using var scratch = new ScriptScratch();
        var dataset = scratch.WriteFile("golden.json", Dataset);
        using var console = new TestConsole();

        var exit = await EvalCommand.ExecuteRunAsync(console, Run(scratch, dataset, o => o.Evaluators = "similarity,bogus"));

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("unknown evaluator 'bogus'", console.Stderr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(scratch.Root, ".orkeon")), "a host was built for a refused line");
    }

    [Fact]
    [Trait("BR", "FWK-09")]
    public async Task ShouldRefuseTheJudgeOutOfTheSuite_WhenLlmJudgeIsNotAsked()
    {
        using var scratch = new ScriptScratch();
        var dataset = scratch.WriteFile("golden.json", Dataset);
        using var console = new TestConsole();

        var exit = await EvalCommand.ExecuteRunAsync(console, Run(scratch, dataset, o => o.Evaluators = "coherence"));

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("--llm-judge", console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-09")]
    public async Task ShouldRefuseLlmJudgeWithOffline()
    {
        using var scratch = new ScriptScratch();
        var dataset = scratch.WriteFile("golden.json", Dataset);
        using var console = new TestConsole();

        var exit = await EvalCommand.ExecuteRunAsync(console, Run(scratch, dataset, o => { o.LlmJudge = true; o.Offline = true; }));

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("--offline", console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-09")]
    public async Task ShouldRefuseACaseWithoutOutput_WhenNoCrewIsGiven()
    {
        using var scratch = new ScriptScratch();
        var dataset = scratch.WriteFile("nooutput.json", """[ { "task_description": "x", "expected_output": "y" } ]""");
        using var console = new TestConsole();

        var exit = await EvalCommand.ExecuteRunAsync(console, Run(scratch, dataset));

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("case 0", console.Stderr, StringComparison.Ordinal);
        Assert.Contains("--crew", console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-09")]
    public async Task ShouldRefuseAScoreOutOfRange()
    {
        using var scratch = new ScriptScratch();
        var dataset = scratch.WriteFile("golden.json", Dataset);
        using var console = new TestConsole();

        var exit = await EvalCommand.ExecuteRunAsync(console, Run(scratch, dataset, o => o.MinScore = 1.5));

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("--min-score", console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-09")]
    public async Task ShouldWriteTheBenchmarkReports_AndExitOne_WhenTheBaselineRegresses()
    {
        using var scratch = new ScriptScratch();
        var dataset = scratch.WriteFile("golden.json", Dataset);
        var settings = scratch.WriteFile("settings.json", "{}");
        using var first = new TestConsole();
        var exit = await EvalCommand.ExecuteBenchAsync(first, new EvalBenchOptions
        {
            Dataset = dataset, SettingsPath = settings, Evaluators = "similarity", Runs = 2, WorkingDirectoryOverride = scratch.Root,
        });
        Assert.True(exit == Program.ExitOk, first.Stderr);
        using var benchmark = Report(scratch, "golden-bench.json");
        Assert.Equal(2, benchmark.RootElement.GetProperty("runs_per_case").GetInt32());
        Assert.True(File.Exists(Path.Combine(scratch.Root, ".orkeon", "eval", "golden-bench.md")));

        // A baseline that scored a perfect 1.0 on an evaluator the dataset now fails; kept out of the
        // folder the run writes to.
        Directory.CreateDirectory(Path.Combine(scratch.Root, "baselines"));
        Directory.CreateDirectory(Path.Combine(scratch.Root, "next"));
        var baseline = Path.Combine(scratch.Root, "baselines", "golden-bench.json");
        File.Copy(Path.Combine(scratch.Root, ".orkeon", "eval", "golden-bench.json"), baseline);
        var worse = scratch.WriteFile(Path.Combine("next", "golden.json"), """[ { "expected_output": "alpha beta gamma", "output": "zzz qqq" } ]""");
        using var second = new TestConsole();
        var regressed = await EvalCommand.ExecuteBenchAsync(second, new EvalBenchOptions
        {
            Dataset = worse, SettingsPath = settings, Evaluators = "similarity", Runs = 1, Baseline = baseline, WorkingDirectoryOverride = scratch.Root,
        });

        Assert.Equal(Program.ExitScriptError, regressed);
        Assert.Contains("REGRESSION", second.Stderr, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("BR", "FWK-09")]
    [InlineData(".orkeon/eval/golden-bench.json")]
    [InlineData("/output/eval/golden-bench.json")]
    public async Task ShouldRefuseABaselineThatIsTheReportTheRunWrites_AndLeaveItUntouched(string baselineArgument)
    {
        // The documented usage used to overwrite the baseline with the run, then compare the run with
        // itself: never a regression.
        using var scratch = new ScriptScratch();
        var settings = scratch.WriteFile("settings.json", "{}");
        var dataset = scratch.WriteFile("golden.json", Dataset);
        using (var first = new TestConsole())
        {
            Assert.Equal(Program.ExitOk, await EvalCommand.ExecuteBenchAsync(first, new EvalBenchOptions
            {
                Dataset = dataset, SettingsPath = settings, Evaluators = "similarity", Runs = 1, WorkingDirectoryOverride = scratch.Root,
            }));
        }

        var baselineFile = Path.Combine(scratch.Root, ".orkeon", "eval", "golden-bench.json");
        var before = await File.ReadAllTextAsync(baselineFile, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(dataset, """[ { "expected_output": "alpha beta gamma", "output": "zzz qqq" } ]""", TestContext.Current.CancellationToken);
        using var second = new TestConsole();
        var exit = await EvalCommand.ExecuteBenchAsync(second, new EvalBenchOptions
        {
            Dataset = dataset, SettingsPath = settings, Evaluators = "similarity", Runs = 1, Baseline = baselineArgument, WorkingDirectoryOverride = scratch.Root,
        });

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("is the report this run writes", second.Stderr, StringComparison.Ordinal);
        Assert.Equal(before, await File.ReadAllTextAsync(baselineFile, TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("BR", "FWK-09")]
    public async Task ShouldRefuseABaselineOfAnotherDatasetOrEvaluators()
    {
        using var scratch = new ScriptScratch();
        var settings = scratch.WriteFile("settings.json", "{}");
        var other = scratch.WriteFile("other.json", Dataset);
        using (var scoring = new TestConsole())
        {
            Assert.Equal(Program.ExitOk, await EvalCommand.ExecuteRunAsync(scoring, new EvalRunOptions
            {
                Dataset = other, SettingsPath = settings, Evaluators = "similarity", WorkingDirectoryOverride = scratch.Root,
            }));
        }

        var golden = scratch.WriteFile("golden.json", Dataset);
        using var bench = new TestConsole();
        var exit = await EvalCommand.ExecuteBenchAsync(bench, new EvalBenchOptions
        {
            Dataset = golden, SettingsPath = settings, Evaluators = "similarity", Runs = 1,
            Baseline = Path.Combine(scratch.Root, ".orkeon", "eval", "other-eval.json"), WorkingDirectoryOverride = scratch.Root,
        });

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("the dataset 'other'", bench.Stderr, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(scratch.Root, ".orkeon", "eval", "golden-bench.json")));
    }

    [Theory]
    [Trait("BR", "FWK-09")]
    [InlineData(-0.1)]
    [InlineData(double.NaN)]
    public async Task ShouldRefuseAThresholdOutOfRange(double threshold)
    {
        using var scratch = new ScriptScratch();
        var settings = scratch.WriteFile("settings.json", "{}");
        var dataset = scratch.WriteFile("golden.json", Dataset);
        using var bench = new TestConsole();
        using var compare = new TestConsole();

        var benchExit = await EvalCommand.ExecuteBenchAsync(bench, new EvalBenchOptions
        {
            Dataset = dataset, SettingsPath = settings, Evaluators = "similarity", Threshold = threshold, WorkingDirectoryOverride = scratch.Root,
        });
        var compareExit = await EvalCommand.ExecuteCompareAsync(compare, new EvalCompareOptions
        {
            Baseline = "a.json", Current = "b.json", Threshold = threshold, SettingsPath = settings, WorkingDirectoryOverride = scratch.Root,
        });

        Assert.Equal(Program.ExitScriptError, benchExit);
        Assert.Contains("--threshold must be between 0 and 1", bench.Stderr, StringComparison.Ordinal);
        Assert.Equal(Program.ExitScriptError, compareExit);
        Assert.Contains("--threshold must be between 0 and 1", compare.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-09")]
    public async Task ShouldCompareTwoReports_AndExitOneOnARegression()
    {
        using var scratch = new ScriptScratch();
        var settings = scratch.WriteFile("settings.json", "{}");
        Directory.CreateDirectory(Path.Combine(scratch.Root, "good"));
        Directory.CreateDirectory(Path.Combine(scratch.Root, "bad"));
        var good = scratch.WriteFile(Path.Combine("good", "qa.json"), Dataset);
        var bad = scratch.WriteFile(Path.Combine("bad", "qa.json"), """[ { "expected_output": "alpha beta gamma", "output": "zzz qqq" } ]""");
        foreach (var (dataset, output) in new[] { (good, "/output/eval/good"), (bad, "/output/eval/bad") })
        {
            using var scoring = new TestConsole();
            Assert.Equal(Program.ExitOk, await EvalCommand.ExecuteRunAsync(scoring, new EvalRunOptions
            {
                Dataset = dataset, SettingsPath = settings, Evaluators = "similarity", Output = output, WorkingDirectoryOverride = scratch.Root,
            }));
        }

        var goodReport = Path.Combine(scratch.Root, ".orkeon", "eval", "good", "qa-eval.json");
        var badReport = Path.Combine(scratch.Root, ".orkeon", "eval", "bad", "qa-eval.json");
        var goodCopy = Path.Combine(scratch.Root, "qa-eval-copy.json");
        File.Copy(goodReport, goodCopy);

        using var itself = new TestConsole();
        var refused = await EvalCommand.ExecuteCompareAsync(itself, new EvalCompareOptions
        {
            Baseline = goodReport, Current = "/output/eval/good/qa-eval.json", SettingsPath = settings, WorkingDirectoryOverride = scratch.Root,
        });
        using var same = new TestConsole();
        var unchanged = await EvalCommand.ExecuteCompareAsync(same, new EvalCompareOptions
        {
            Baseline = goodReport, Current = goodCopy, SettingsPath = settings, WorkingDirectoryOverride = scratch.Root,
        });
        using var worse = new TestConsole();
        var regressed = await EvalCommand.ExecuteCompareAsync(worse, new EvalCompareOptions
        {
            Baseline = goodReport, Current = badReport, SettingsPath = settings, WorkingDirectoryOverride = scratch.Root,
        });

        Assert.Equal(Program.ExitScriptError, refused);
        Assert.Contains("are the same file", itself.Stderr, StringComparison.Ordinal);
        Assert.True(unchanged == Program.ExitOk, same.Stderr);
        Assert.Contains("No Regression", same.Stdout, StringComparison.Ordinal);
        Assert.Equal(Program.ExitScriptError, regressed);
        Assert.Contains("REGRESSION", worse.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-09")]
    public async Task ShouldMaskASecretInTheReport_WhateverTheFieldIs()
    {
        using var scratch = new ScriptScratch();
        // Built at run time: a literal of this shape is what the repository's secret scanners look for.
        var secret = string.Concat("s", "k", "-", "abcdefghijklmnopqrstuvwxyz0123456789");
        var dataset = scratch.WriteFile("leak.json", "[ { \"task_description\": \"key " + secret + "\", \"expected_output\": \"the key is " + secret + "\", \"output\": \"the key is " + secret + "\" } ]");
        using var console = new TestConsole();

        var exit = await EvalCommand.ExecuteRunAsync(console, Run(scratch, dataset));

        Assert.True(exit == Program.ExitOk, console.Stderr);
        var text = await File.ReadAllTextAsync(Path.Combine(scratch.Root, ".orkeon", "eval", "leak-eval.json"), TestContext.Current.CancellationToken);
        Assert.DoesNotContain("abcdefghijkl", text, StringComparison.Ordinal);
        Assert.Contains("***REDACTED***", text, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-09")]
    public async Task ShouldRefuseAnEmptyDataset()
    {
        using var scratch = new ScriptScratch();
        var dataset = scratch.WriteFile("empty.json", "[]");
        using var console = new TestConsole();

        var exit = await EvalCommand.ExecuteRunAsync(console, Run(scratch, dataset));

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("holds no case", console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-09")]
    public async Task ShouldNameTheFileAndThePosition_WhenTheDatasetIsMalformed()
    {
        using var scratch = new ScriptScratch();
        var dataset = scratch.WriteFile("broken.json", "[\n  { \"output\": \"a\" },\n  { \"output\": \n]");
        using var console = new TestConsole();

        var exit = await EvalCommand.ExecuteRunAsync(console, Run(scratch, dataset));

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("broken.json", console.Stderr, StringComparison.Ordinal);
        Assert.Contains("line", console.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("unexpected error", console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-09")]
    public async Task ShouldNameTheFile_WhenTheDatasetDoesNotExist()
    {
        using var scratch = new ScriptScratch();
        using var console = new TestConsole();

        var exit = await EvalCommand.ExecuteRunAsync(console, Run(scratch, "nowhere.json"));

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("nowhere.json", console.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("unexpected error", console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-09")]
    public async Task ShouldScoreOnlyTheNamedEvaluators()
    {
        using var scratch = new ScriptScratch();
        var dataset = scratch.WriteFile("golden.json", Dataset);
        using var console = new TestConsole();

        var exit = await EvalCommand.ExecuteRunAsync(console, Run(scratch, dataset, o => o.Evaluators = "similarity, text-quality"));

        Assert.True(exit == Program.ExitOk, console.Stderr);
        using var report = Report(scratch, "golden-eval.json");
        var scored = report.RootElement.GetProperty("summary").GetProperty("scores_by_evaluator").EnumerateObject().Select(p => p.Name).Order().ToList();
        Assert.Equal(["Similarity", "TextQuality"], scored);
    }

    [Fact]
    [Trait("BR", "FWK-09")]
    public async Task ShouldForceTheJudgesOff_WhenOfflineOverridesTheSettings()
    {
        using var scratch = new ScriptScratch();
        var dataset = scratch.WriteFile("golden.json", Dataset);
        var settings = scratch.WriteFile("judges.json", """{ "Llm": { "Model": "m" }, "Evaluation": { "EnableLlmJudge": true } }""");
        using var console = new TestConsole();

        var exit = await EvalCommand.ExecuteRunAsync(console, new EvalRunOptions
        {
            Dataset = dataset, SettingsPath = settings, Offline = true, WorkingDirectoryOverride = scratch.Root,
        });

        Assert.True(exit == Program.ExitOk, console.Stderr);
        using var report = Report(scratch, "golden-eval.json");
        var scored = report.RootElement.GetProperty("summary").GetProperty("scores_by_evaluator").EnumerateObject().Select(p => p.Name).ToList();
        Assert.DoesNotContain("Coherence", scored);
        Assert.Contains("Similarity", scored);
    }

    [Fact]
    [Trait("BR", "FWK-09")]
    public async Task ShouldSayItOverwritesAnExistingReport()
    {
        using var scratch = new ScriptScratch();
        var dataset = scratch.WriteFile("golden.json", Dataset);
        using var first = new TestConsole();
        await EvalCommand.ExecuteRunAsync(first, Run(scratch, dataset));
        using var second = new TestConsole();

        var exit = await EvalCommand.ExecuteRunAsync(second, Run(scratch, dataset));

        Assert.Equal(Program.ExitOk, exit);
        Assert.DoesNotContain("overwrit", first.Stdout, StringComparison.Ordinal);
        Assert.Contains("overwritten", second.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-09")]
    public async Task ShouldGiveBenchAndCompareTheirExitCodes()
    {
        using var scratch = new ScriptScratch();
        var dataset = scratch.WriteFile("golden.json", Dataset);
        var settings = scratch.WriteFile("settings.json", "{}");
        var notAReport = scratch.WriteFile("not-a-report.json", "{ \"nothing\": 1 }");

        using var runs = new TestConsole();
        var noRuns = await EvalCommand.ExecuteBenchAsync(runs, new EvalBenchOptions { Dataset = dataset, SettingsPath = settings, Runs = 0, WorkingDirectoryOverride = scratch.Root });
        using var gate = new TestConsole();
        var belowGate = await EvalCommand.ExecuteBenchAsync(gate, new EvalBenchOptions
        {
            Dataset = scratch.WriteFile("bad.json", """[ { "expected_output": "alpha beta gamma", "output": "zzz qqq" } ]"""),
            SettingsPath = settings, Evaluators = "similarity", Runs = 1, MinScore = 0.99, WorkingDirectoryOverride = scratch.Root,
        });
        using var notReport = new TestConsole();
        var unreadable = await EvalCommand.ExecuteCompareAsync(notReport, new EvalCompareOptions
        {
            Baseline = notAReport, Current = notAReport, SettingsPath = settings, WorkingDirectoryOverride = scratch.Root,
        });
        using var missing = new TestConsole();
        var absent = await EvalCommand.ExecuteCompareAsync(missing, new EvalCompareOptions
        {
            Baseline = "absent.json", Current = "absent.json", SettingsPath = settings, WorkingDirectoryOverride = scratch.Root,
        });

        Assert.Equal(Program.ExitScriptError, noRuns);
        Assert.Contains("--runs", runs.Stderr, StringComparison.Ordinal);
        Assert.Equal(Program.ExitScriptError, belowGate);
        Assert.Contains("BELOW THRESHOLD", gate.Stderr, StringComparison.Ordinal);
        Assert.Equal(Program.ExitScriptError, unreadable);
        Assert.Contains("not-a-report.json", notReport.Stderr, StringComparison.Ordinal);
        Assert.Equal(Program.ExitScriptError, absent);
        Assert.Contains("absent.json", missing.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-09")]
    public async Task ShouldKnowTheEvalVerb_FromTheEntryTheBinaryUses()
    {
        using var console = new TestConsole();

        var exit = await Program.DispatchAsync(console, ["eval", "run"]);

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("--dataset", console.Stderr, StringComparison.Ordinal);
    }
}
