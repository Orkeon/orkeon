using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Orkeon.Infrastructure.Runs;
using Orkeon.Infrastructure.Security;
using CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orkeon.Application.Evaluation;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.FileSystem;
using Orkeon.Hosting;
using Orkeon.Infrastructure.Evaluation;

namespace Orkeon.Scripting.Cli.Commands;

/// <summary>Options shared by the verbs that score a dataset (<c>eval run</c>, <c>eval bench</c>).</summary>
internal abstract class EvalScoringOptions : RagCommandOptionsBase
{
    /// <summary>The JSON dataset: an array of cases (<c>task_description</c>, <c>expected_output</c>, <c>output</c>, ...).</summary>
    [Option('d', "dataset", Required = true,
        HelpText = "Evaluation dataset: a JSON array of cases with task_description, expected_output, output, context, expected_format, metadata. "
                   + "Without --crew every case needs its output.")]
    public string Dataset { get; set; } = string.Empty;

    /// <summary>Comma-separated evaluator names; every evaluator of the default suite when omitted.</summary>
    [Option("evaluators", Required = false,
        HelpText = "Comma-separated evaluators of the default suite: format, schema, text-quality, similarity, tool-accuracy, "
                   + "and with --llm-judge coherence, fluency, groundedness. Default: all the registered ones.")]
    public string? Evaluators { get; set; }

    /// <summary>Adds the LLM-as-Judge evaluators to the suite.</summary>
    [Option("llm-judge", Required = false, Default = false,
        HelpText = "Add the LLM-as-Judge evaluators (coherence, fluency, groundedness): sets Evaluation:EnableLlmJudge for this run. Needs an LLM.")]
    public bool LlmJudge { get; set; }

    /// <summary>Zero-network run: no LLM judge, and a crew must answer from the mock provider.</summary>
    [Option("offline", Required = false, Default = false,
        HelpText = "Zero-network run: the LLM judges stay off (refused with --llm-judge), and a --crew must run on the mock provider "
                   + "(--llm-mock <file>, or Llm:Provider: mock in the settings).")]
    public bool Offline { get; set; }

    /// <summary>Virtual directory of the reports.</summary>
    [Option("output", Required = false, Default = "/output/eval",
        HelpText = "Virtual directory receiving the JSON report (default /output/eval -> ./.orkeon/eval).")]
    public string Output { get; set; } = "/output/eval";

    /// <summary>Gate on the overall score.</summary>
    [Option("min-score", Required = false,
        HelpText = "Gate: exit 1 when the overall score (between 0 and 1) is under this threshold.")]
    public double? MinScore { get; set; }
}

/// <summary>Parsed CLI options for <c>orkeon eval run</c>.</summary>
[Verb("run", HelpText = "Score a dataset with the default evaluation suite; with --crew, run a crew on every case first. Writes a JSON report.")]
internal sealed class EvalRunOptions : EvalScoringOptions
{
    /// <summary>The crew run on every case; its final output replaces the case's output in the report.</summary>
    [Option("crew", Required = false,
        HelpText = "Run this crew (.yaml, .ork.ts or a crew directory) on every case before scoring: the case's task_description is the crew's prompt "
                   + "(the input and the initial context), its final output replaces output in the report. The dataset file is never modified.")]
    public string? Crew { get; set; }

    /// <summary>Responses file of the mock provider.</summary>
    [Option("llm-mock", Required = false,
        HelpText = "With --crew: answer every LLM call of the crew from this JSONL file, offline (as orkeon run --llm-mock).")]
    public string? LlmMock { get; set; }

    /// <summary>With <c>--crew</c>: record every LLM call of the crew, with its answer, in this JSONL cassette.</summary>
    [Option("llm-record", Required = false,
        HelpText = "With --crew: record every LLM call of the crew in this JSONL cassette (as orkeon run --llm-record). Exclusive with --llm-replay and --llm-mock.")]
    public string? LlmRecord { get; set; }

    /// <summary>With <c>--crew</c>: answer every LLM call of the crew from this cassette, with no network.</summary>
    [Option("llm-replay", Required = false,
        HelpText = "With --crew: answer every LLM call of the crew from this cassette, recorded by --llm-record, with no network (as orkeon run --llm-replay). Exclusive with --llm-record and --llm-mock.")]
    public string? LlmReplay { get; set; }

    /// <summary>Host LLM profile of the crew.</summary>
    [Option("llm-profile", Required = false, HelpText = "With --crew: run the crew on this host LLM profile (as orkeon run --llm-profile).")]
    public string? LlmProfile { get; set; }
}

/// <summary>Parsed CLI options for <c>orkeon eval bench</c>.</summary>
[Verb("bench", HelpText = "Score a dataset several times per case and report mean and standard deviation; compare with a baseline report.")]
internal sealed class EvalBenchOptions : EvalScoringOptions
{
    /// <summary>Runs per case.</summary>
    [Option("runs", Required = false, Default = 3, HelpText = "Runs of the suite per case (default 3).")]
    public int Runs { get; set; } = 3;

    /// <summary>A baseline benchmark or evaluation report to compare with.</summary>
    [Option("baseline", Required = false,
        HelpText = "A report written by eval bench or eval run: the run is compared with it, and exits 1 on a regression.")]
    public string? Baseline { get; set; }

    /// <summary>Score drop that counts as a regression.</summary>
    [Option("threshold", Required = false, Default = 0.05, HelpText = "Absolute score drop that counts as a regression (default 0.05).")]
    public double Threshold { get; set; } = 0.05;
}

/// <summary>Parsed CLI options for <c>orkeon eval compare</c>.</summary>
[Verb("compare", HelpText = "Compare two reports written by eval run or eval bench; exit 1 on a regression.")]
internal sealed class EvalCompareOptions : RagCommandOptionsBase
{
    /// <summary>The baseline report.</summary>
    [Value(0, Required = true, MetaName = "baseline", HelpText = "The baseline report (JSON).")]
    public string Baseline { get; set; } = string.Empty;

    /// <summary>The current report.</summary>
    [Value(1, Required = true, MetaName = "current", HelpText = "The current report (JSON).")]
    public string Current { get; set; } = string.Empty;

    /// <summary>Score drop that counts as a regression.</summary>
    [Option("threshold", Required = false, Default = 0.05, HelpText = "Absolute score drop that counts as a regression (default 0.05).")]
    public double Threshold { get; set; } = 0.05;

    /// <summary>Writes the comparison as JSON instead of markdown.</summary>
    [Option("json", Required = false, Default = false, HelpText = "Print the comparison as JSON instead of markdown.")]
    public bool Json { get; set; }
}

/// <summary>
/// <c>orkeon eval run | bench | compare | rag</c> (FWK-09): the evaluation framework as a command. The
/// scoring verbs build the shared runner host (<see cref="RunnerHost.Build"/>), which binds the
/// <c>Evaluation</c> section, so the suite is the default one with the judges the settings or
/// <c>--llm-judge</c> ask for; <c>--crew</c> runs a crew per case over a second host of its own, built by
/// <c>orkeon run</c>'s guards. <c>eval rag</c> is the RAG evaluation that used to be <c>rag eval</c>.
/// </summary>
internal static class EvalCommand
{
    /// <summary>The evaluators a name on the command line may designate, by normalised name.</summary>
    private static readonly Dictionary<string, string> KnownEvaluators = new(StringComparer.Ordinal)
    {
        ["FORMAT"] = "FormatCompliance",
        ["FORMATCOMPLIANCE"] = "FormatCompliance",
        ["SCHEMA"] = "SchemaCompliance",
        ["SCHEMACOMPLIANCE"] = "SchemaCompliance",
        ["TEXTQUALITY"] = "TextQuality",
        ["SIMILARITY"] = "Similarity",
        ["TOOLACCURACY"] = "ToolAccuracy",
        ["COHERENCE"] = "Coherence",
        ["FLUENCY"] = "Fluency",
        ["GROUNDEDNESS"] = "Groundedness",
    };

    private static readonly JsonSerializerOptions s_cases = new() { WriteIndented = true };

    /// <summary>Parses <paramref name="args"/> (already stripped of the leading <c>eval</c>) and dispatches to a verb.</summary>
    public static async Task<int> DispatchAsync(IRunnerConsole console, string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        return await CliGrammar.DispatchAsync(
            console,
            "eval",
            args,
            [typeof(EvalRunOptions), typeof(EvalBenchOptions), typeof(EvalCompareOptions), typeof(RagEvalCommandOptions)],
            (console, options) => options switch
            {
                EvalRunOptions o => ExecuteRunAsync(console, o),
                EvalBenchOptions o => ExecuteBenchAsync(console, o),
                EvalCompareOptions o => ExecuteCompareAsync(console, o),
                RagEvalCommandOptions o => RagCommand.ExecuteEvalAsync(console, o),
                _ => System.Threading.Tasks.Task.FromResult(Program.ExitScriptError),
            }).ConfigureAwait(false);
    }

    /// <summary>Scores the dataset, running <c>--crew</c> on every case first; returns the CLI exit code.</summary>
    public static System.Threading.Tasks.Task<int> ExecuteRunAsync(IRunnerConsole console, EvalRunOptions options)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(options);
        return RagCommand.GuardedAsync(console, "eval run", async ct =>
        {
            if (await RefuseAsync(console, "eval run", options).ConfigureAwait(false) is { } refused)
                return refused;

            if (options.Crew is null && (options.LlmMock is not null || options.LlmRecord is not null || options.LlmReplay is not null || options.LlmProfile is not null))
            {
                await console.ErrorOutput.WriteLineAsync("orkeon eval run: --llm-mock, --llm-record, --llm-replay and --llm-profile belong to --crew.").ConfigureAwait(false);
                return Program.ExitScriptError;
            }

            var cwd = RagCommand.ResolveCwd(options);
            using var host = BuildScoringHost(console, options, cwd);
            if (host is null)
                return Program.ExitScriptError;

            var loaded = await LoadAsync(console, "eval run", host, options, cwd, requireOutput: options.Crew is null, ct).ConfigureAwait(false);
            if (loaded is null)
                return Program.ExitScriptError;

            var (dataset, suite, inputs) = loaded.Value;
            var produced = inputs;
            if (options.Crew is not null)
            {
                var crewRun = await RunCrewPerCaseAsync(console, options, cwd, inputs, ct).ConfigureAwait(false);
                if (crewRun.ExitCode is { } code)
                    return code;
                produced = crewRun.Inputs!;
            }

            var report = await suite.RunAsync(produced, ct).ConfigureAwait(false);
            var path = $"{options.Output.TrimEnd('/')}/{ReportName(dataset)}-eval.json";
            var fileSystem = host.Services.GetRequiredService<IFileSystemService>();
            await WriteReportAsync(console, fileSystem, path, ReportJson(dataset, report, produced), ct).ConfigureAwait(false);

            await PrintSummaryAsync(console, dataset, report.Summary.TotalCases, report.Summary.OverallScore, report.Summary.ScoresByEvaluator, path).ConfigureAwait(false);
            return await GateAsync(console, "eval run", report.Summary.OverallScore, options.MinScore).ConfigureAwait(false);
        });
    }

    /// <summary>Scores the dataset several times per case, optionally against a baseline; returns the CLI exit code.</summary>
    public static System.Threading.Tasks.Task<int> ExecuteBenchAsync(IRunnerConsole console, EvalBenchOptions options)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(options);
        return RagCommand.GuardedAsync(console, "eval bench", async ct =>
        {
            if (await RefuseAsync(console, "eval bench", options).ConfigureAwait(false) is { } refused)
                return refused;

            if (options.Runs < 1)
            {
                await console.ErrorOutput.WriteLineAsync("orkeon eval bench: --runs must be at least 1.").ConfigureAwait(false);
                return Program.ExitScriptError;
            }

            if (await RefuseThresholdAsync(console, "eval bench", options.Threshold).ConfigureAwait(false) is { } badThreshold)
                return badThreshold;

            var cwd = RagCommand.ResolveCwd(options);
            using var host = BuildScoringHost(console, options, cwd);
            if (host is null)
                return Program.ExitScriptError;

            var loaded = await LoadAsync(console, "eval bench", host, options, cwd, requireOutput: true, ct).ConfigureAwait(false);
            if (loaded is null)
                return Program.ExitScriptError;

            var (name, suite, inputs) = loaded.Value;
            var fileSystem = host.Services.GetRequiredService<IFileSystemService>();
            var jsonPath = $"{options.Output.TrimEnd('/')}/{ReportName(name)}-bench.json";

            // The baseline is read before the run writes anything: the report this run writes could be
            // the baseline itself (the default --output is ./.orkeon/eval), and overwritten first it
            // would compare with itself and never regress.
            BenchmarkReport? baseline = null;
            if (options.Baseline is not null)
            {
                if (SameFile(fileSystem, options.Baseline, cwd, jsonPath))
                {
                    await console.ErrorOutput.WriteLineAsync(
                        $"orkeon eval bench: --baseline '{options.Baseline}' is the report this run writes ({jsonPath}): keep the baseline elsewhere (copy it, or pass another --output).")
                        .ConfigureAwait(false);
                    return Program.ExitScriptError;
                }

                baseline = await ReadReportAsync(fileSystem, options.Baseline, cwd, ct).ConfigureAwait(false);
            }

            var runner = host.Services.GetRequiredService<IBenchmarkRunner>();
            var report = await runner.RunAsync(
                new BenchmarkConfig(new InMemoryDataset(name, inputs), suite, options.Runs, options.Baseline), ct).ConfigureAwait(false);

            if (baseline is not null && CompareReports.Incompatibility(baseline, report) is { } reason)
            {
                await console.ErrorOutput.WriteLineAsync($"orkeon eval bench: the baseline '{options.Baseline}' cannot be compared with this run: {reason}.")
                    .ConfigureAwait(false);
                return Program.ExitScriptError;
            }

            await WriteReportAsync(console, fileSystem, jsonPath, Redacted(ReportExporter.ToJson(report)), ct).ConfigureAwait(false);
            await WriteReportAsync(
                console, fileSystem, $"{options.Output.TrimEnd('/')}/{ReportName(name)}-bench.md", LogSanitizer.SanitizeString(ReportExporter.ToMarkdown(report)), ct)
                .ConfigureAwait(false);

            await PrintSummaryAsync(console, name, report.Results.Count, report.Summary.OverallMean, report.Summary.MeanByEvaluator, jsonPath).ConfigureAwait(false);

            var exit = await GateAsync(console, "eval bench", report.Summary.OverallMean, options.MinScore).ConfigureAwait(false);
            if (baseline is null)
                return exit;

            var comparison = CompareReports.Compare(baseline, report, options.Threshold);
            await console.Output.WriteAsync(ReportExporter.ToMarkdown(comparison)).ConfigureAwait(false);
            if (!comparison.HasRegression)
                return exit;

            await console.ErrorOutput.WriteLineAsync(Inv(
                $"orkeon eval bench: REGRESSION - score {comparison.CurrentScore:F4} against baseline {comparison.BaselineScore:F4} (delta {comparison.ScoreDelta:+0.0000;-0.0000;0.0000}).")).ConfigureAwait(false);
            return Program.ExitScriptError;
        });
    }

    /// <summary>Compares two reports; returns the CLI exit code: 1 on a regression.</summary>
    public static System.Threading.Tasks.Task<int> ExecuteCompareAsync(IRunnerConsole console, EvalCompareOptions options)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(options);
        return RagCommand.GuardedAsync(console, "eval compare", async ct =>
        {
            if (await RefuseThresholdAsync(console, "eval compare", options.Threshold).ConfigureAwait(false) is { } badThreshold)
                return badThreshold;

            var cwd = RagCommand.ResolveCwd(options);
            using var host = RagCommand.BuildHost(console, options, cwd);
            if (host is null)
                return Program.ExitScriptError;

            var fileSystem = host.Services.GetRequiredService<IFileSystemService>();
            var currentPath = RagCommand.ToVirtualSource(options.Current, cwd, fileSystem.GetAvailableMounts());
            if (SameFile(fileSystem, options.Baseline, cwd, currentPath))
            {
                await console.ErrorOutput.WriteLineAsync($"orkeon eval compare: the baseline and the current report are the same file ('{options.Current}').")
                    .ConfigureAwait(false);
                return Program.ExitScriptError;
            }

            var baseline = await ReadReportAsync(fileSystem, options.Baseline, cwd, ct).ConfigureAwait(false);
            var current = await ReadReportAsync(fileSystem, options.Current, cwd, ct).ConfigureAwait(false);
            if (CompareReports.Incompatibility(baseline, current) is { } reason)
            {
                await console.ErrorOutput.WriteLineAsync($"orkeon eval compare: the reports cannot be compared: {reason}.").ConfigureAwait(false);
                return Program.ExitScriptError;
            }

            var comparison = CompareReports.Compare(baseline, current, options.Threshold);

            await console.Output.WriteAsync(options.Json ? ReportExporter.ToJson(comparison) + Environment.NewLine : ReportExporter.ToMarkdown(comparison))
                .ConfigureAwait(false);
            if (!comparison.HasRegression)
                return Program.ExitOk;

            await console.ErrorOutput.WriteLineAsync("orkeon eval compare: REGRESSION detected.").ConfigureAwait(false);
            return Program.ExitScriptError;
        });
    }

    /// <summary>Refuses a --threshold that is negative, above 1 or not a number; null when it is valid.</summary>
    private static async Task<int?> RefuseThresholdAsync(IRunnerConsole console, string command, double threshold)
    {
        if (CompareReports.IsValidThreshold(threshold))
            return null;

        await console.ErrorOutput.WriteLineAsync($"orkeon {command}: --threshold must be between 0 and 1.").ConfigureAwait(false);
        return Program.ExitScriptError;
    }

    /// <summary>
    /// True when the report path <paramref name="raw"/> (as the user typed it) and the virtual path
    /// <paramref name="otherVirtual"/> name the same file: compared on their physical paths, since two
    /// mounts can reach one folder (<c>/output</c> is <c>./.orkeon</c>, also under <c>/workspace</c>).
    /// </summary>
    private static bool SameFile(IFileSystemService fileSystem, string raw, string cwd, string otherVirtual)
    {
        var virtualPath = RagCommand.ToVirtualSource(raw, cwd, fileSystem.GetAvailableMounts());
        if (string.Equals(virtualPath.TrimEnd('/'), otherVirtual.TrimEnd('/'), StringComparison.Ordinal))
            return true;

        var one = fileSystem.ResolveAndValidate(virtualPath, FileAccessRights.Read);
        var other = fileSystem.ResolveAndValidate(otherVirtual, FileAccessRights.Read);
        if (one.ResolvedPath is not { } first || other.ResolvedPath is not { } second)
            return false;

        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), comparison);
    }

    /// <summary>The refusals that need no host: an unknown evaluator, both judge switches, a score out of range.</summary>
    private static async Task<int?> RefuseAsync(IRunnerConsole console, string command, EvalScoringOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Dataset))
        {
            await console.ErrorOutput.WriteLineAsync($"orkeon {command}: --dataset is required.").ConfigureAwait(false);
            return Program.ExitScriptError;
        }

        if (options.LlmJudge && options.Offline)
        {
            await console.ErrorOutput.WriteLineAsync($"orkeon {command}: --llm-judge needs an LLM, --offline forbids one: pick one.").ConfigureAwait(false);
            return Program.ExitScriptError;
        }

        if (options.MinScore is { } min && (min is < 0 or > 1 || double.IsNaN(min)))
        {
            await console.ErrorOutput.WriteLineAsync($"orkeon {command}: --min-score must be between 0 and 1.").ConfigureAwait(false);
            return Program.ExitScriptError;
        }

        if (options.Evaluators is not null && ParseEvaluators(options.Evaluators, out var unknown) is null)
        {
            await console.ErrorOutput.WriteLineAsync(
                $"orkeon {command}: unknown evaluator '{unknown}'. Known: format, schema, text-quality, similarity, tool-accuracy, coherence, fluency, groundedness.")
                .ConfigureAwait(false);
            return Program.ExitScriptError;
        }

        return null;
    }

    /// <summary>The canonical evaluator names of <paramref name="list"/>, or null (and the first unknown token) when one is unknown.</summary>
    private static List<string>? ParseEvaluators(string list, out string? unknown)
    {
        unknown = null;
        var names = new List<string>();
        foreach (var token in list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var key = token.Replace("-", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
            if (!KnownEvaluators.TryGetValue(key, out var name))
            {
                unknown = token;
                return null;
            }

            if (!names.Contains(name, StringComparer.Ordinal))
                names.Add(name);
        }

        if (names.Count == 0)
        {
            unknown = list;
            return null;
        }

        return names;
    }

    /// <summary>The shared runner host, with the judge switch the options ask for laid over the settings.</summary>
    private static IHost? BuildScoringHost(IRunnerConsole console, EvalScoringOptions options, string cwd)
    {
        var judge = options.LlmJudge ? "true" : options.Offline ? "false" : null;
        return RagCommand.BuildHost(
            console,
            options,
            cwd,
            judge is null
                ? null
                : builder => builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection([new KeyValuePair<string, string?>("Evaluation:EnableLlmJudge", judge)])));
    }

    /// <summary>Loads the dataset and picks the evaluators; writes the refusal and returns null when either fails.</summary>
    private static async Task<(string Name, IEvaluationSuite Suite, IReadOnlyList<EvaluationInput> Inputs)?> LoadAsync(
        IRunnerConsole console,
        string command,
        IHost host,
        EvalScoringOptions options,
        string cwd,
        bool requireOutput,
        CancellationToken ct)
    {
        var fileSystem = host.Services.GetRequiredService<IFileSystemService>();
        var virtualPath = RagCommand.ToVirtualSource(options.Dataset, cwd, fileSystem.GetAvailableMounts());
        var name = Path.GetFileNameWithoutExtension(options.Dataset.TrimEnd('/', '\\'));
        IReadOnlyList<EvaluationInput> inputs;
        try
        {
            inputs = await new JsonFileDataset(fileSystem, name, virtualPath).LoadAsync(ct).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            await console.ErrorOutput.WriteLineAsync($"orkeon {command}: the dataset '{options.Dataset}' does not exist.").ConfigureAwait(false);
            return null;
        }
        catch (JsonException ex)
        {
            var where = ex.LineNumber is { } line ? $" at line {line + 1}, position {(ex.BytePositionInLine ?? 0) + 1}" : string.Empty;
            await console.ErrorOutput.WriteLineAsync(
                $"orkeon {command}: the dataset '{options.Dataset}' is not a JSON array of cases{where}: {ex.Message}").ConfigureAwait(false);
            return null;
        }

        if (inputs.Count == 0)
        {
            await console.ErrorOutput.WriteLineAsync($"orkeon {command}: the dataset '{options.Dataset}' holds no case.").ConfigureAwait(false);
            return null;
        }

        if (requireOutput)
        {
            for (var i = 0; i < inputs.Count; i++)
            {
                if (!string.IsNullOrEmpty(inputs[i].Output))
                    continue;

                await console.ErrorOutput.WriteLineAsync(
                    $"orkeon {command}: case {i} of '{options.Dataset}' has no output to score; give it one, or run a crew with --crew.").ConfigureAwait(false);
                return null;
            }
        }

        var suite = host.Services.GetRequiredService<IEvaluationSuite>();
        if (options.Evaluators is not null)
        {
            var wanted = ParseEvaluators(options.Evaluators, out _)!;
            var missing = wanted.Where(w => !suite.Evaluators.Any(e => string.Equals(e.Name, w, StringComparison.Ordinal))).ToList();
            if (missing.Count > 0)
            {
                await console.ErrorOutput.WriteLineAsync(
                    $"orkeon {command}: evaluator(s) {string.Join(", ", missing)} are not in the suite: the LLM judges need --llm-judge (or Evaluation:EnableLlmJudge).")
                    .ConfigureAwait(false);
                return null;
            }

            suite = new EvaluationSuite(suite.Name, suite.Evaluators.Where(e => wanted.Contains(e.Name, StringComparer.Ordinal)));
        }

        return (name, suite, inputs);
    }

    /// <summary>Runs <c>--crew</c> on every case, over one host, and returns the cases with the crew's output in place of theirs.</summary>
    private static async Task<(int? ExitCode, IReadOnlyList<EvaluationInput>? Inputs)> RunCrewPerCaseAsync(
        IRunnerConsole console,
        EvalRunOptions options,
        string cwd,
        IReadOnlyList<EvaluationInput> inputs,
        CancellationToken ct)
    {
        var crewOptions = new EvalCrewOptions
        {
            ConfigPath = Path.GetFullPath(options.Crew!, cwd),
            SettingsPath = options.SettingsPath,
            Mounts = [.. options.Mounts],
            AllowExternalMounts = options.AllowExternalMounts,
            LlmMock = options.LlmMock,
            LlmRecord = options.LlmRecord,
            LlmReplay = options.LlmReplay,
            LlmProfile = options.LlmProfile,
            Verbose = options.Verbose,
            RunnerConsole = console,
            WorkingDirectory = cwd,
        };

        if (!CrewRunEntryPoint.TryOpen(crewOptions, "Orkeon.Eval", options.ConfigureTestServices, out var entry, out var errorCode))
            return (errorCode, null);

        using (entry)
        {
            var produced = new List<EvaluationInput>(inputs.Count);
            for (var i = 0; i < inputs.Count; i++)
            {
                var prompt = inputs[i].TaskDescription;
                if (string.IsNullOrWhiteSpace(prompt))
                {
                    await console.ErrorOutput.WriteLineAsync(
                        $"orkeon eval run: case {i} has no task_description: with --crew it is the crew's prompt.").ConfigureAwait(false);
                    return (Program.ExitScriptError, null);
                }

                var run = await entry!.RunAsync(
                    (_, _) => System.Threading.Tasks.Task.FromResult<CrewInput?>(
                        new CrewInput(prompt, new Dictionary<string, object> { ["input"] = prompt })),
                    resume: null,
                    ct,
                    options.Offline).ConfigureAwait(false);

                if (run.Outcome == CrewRunOutcome.Refused)
                {
                    await console.ErrorOutput.WriteLineAsync($"orkeon eval run: --offline refused: {run.Refusal}").ConfigureAwait(false);
                    return (Program.ExitScriptError, null);
                }

                if (run.Outcome == CrewRunOutcome.LoadFailed)
                {
                    await console.ErrorOutput.WriteLineAsync($"orkeon eval run: the crew does not load: {run.LoadError!.Message}").ConfigureAwait(false);
                    return (Program.ExitScriptError, null);
                }

                if (run.Output is not { Succeeded: true } output)
                {
                    await console.ErrorOutput.WriteLineAsync(
                        $"orkeon eval run: the crew failed on case {i}: {LogSanitizer.SanitizeString(run.Output?.Error ?? run.Output?.FinalOutput ?? string.Empty)}. "
                        + "The run stops here and no report is written: a report of part of the dataset would read as the whole.").ConfigureAwait(false);
                    return (Program.ExitRuntimeError, null);
                }

                produced.Add(inputs[i] with { Output = output.FinalOutput });
            }

            return (null, produced);
        }
    }

    /// <summary>
    /// <paramref name="json"/> with every string passed through the redaction of the run history
    /// (<see cref="RedactingRunEventSink.Redact"/>): the report holds what a model wrote.
    /// </summary>
    private static string Redacted(string json) =>
        RedactingRunEventSink.Redact(JsonNode.Parse(json))!.ToJsonString(s_cases);

    private static async Task WriteReportAsync(IRunnerConsole console, IFileSystemService fileSystem, string path, string content, CancellationToken ct)
    {
        var existed = await fileSystem.ExistsAsync(path, ct).ConfigureAwait(false);
        await fileSystem.WriteAllTextAsync(path, content, ct).ConfigureAwait(false);
        if (existed)
            await console.Output.WriteLineAsync($"{path} existed and is overwritten.").ConfigureAwait(false);
    }

    /// <summary>The report as JSON, with the cases the suite scored (the produced output, not the dataset's).</summary>
    private static string ReportJson(string dataset, EvaluationReport report, IReadOnlyList<EvaluationInput> scored)
    {
        var root = JsonNode.Parse(ReportExporter.ToJson(report))!.AsObject();
        // The dataset's name: eval compare and eval bench --baseline refuse reports of different datasets.
        root["dataset"] = dataset;
        var cases = new JsonArray();
        for (var i = 0; i < scored.Count; i++)
        {
            cases.Add(new JsonObject
            {
                ["case_index"] = i,
                ["task_description"] = scored[i].TaskDescription,
                ["expected_output"] = scored[i].ExpectedOutput,
                ["output"] = scored[i].Output,
            });
        }

        root["cases"] = cases;
        return Redacted(root.ToJsonString(s_cases));
    }

    private static async Task<BenchmarkReport> ReadReportAsync(IFileSystemService fileSystem, string path, string cwd, CancellationToken ct)
    {
        var virtualPath = RagCommand.ToVirtualSource(path, cwd, fileSystem.GetAvailableMounts());
        var json = await fileSystem.TryReadAllTextAsync(virtualPath, ct).ConfigureAwait(false)
                   ?? throw new FileNotFoundException($"Report not found: {path}", path);
        try
        {
            return ReportExporter.ReadComparableReport(json);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException($"'{path}' is not a report of eval run or eval bench: {ex.Message}", ex);
        }
    }

    private static async Task PrintSummaryAsync(
        IRunnerConsole console, string dataset, int cases, double overall, IReadOnlyDictionary<string, double> byEvaluator, string reportPath)
    {
        var output = console.Output;
        await output.WriteLineAsync(Inv($"Evaluation - dataset '{dataset}', {cases} case(s)")).ConfigureAwait(false);
        await output.WriteLineAsync(Inv($"Overall score: {overall:F4}")).ConfigureAwait(false);
        foreach (var (name, mean) in byEvaluator)
            await output.WriteLineAsync(Inv($"  {name}: {mean:F4}")).ConfigureAwait(false);
        await output.WriteLineAsync($"Report: {reportPath}").ConfigureAwait(false);
    }

    private static async Task<int> GateAsync(IRunnerConsole console, string command, double overall, double? minScore)
    {
        if (minScore is not { } min || overall >= min)
            return Program.ExitOk;

        await console.ErrorOutput.WriteLineAsync(Inv($"orkeon {command}: BELOW THRESHOLD - overall score {overall:F4} < --min-score {min:F4}.")).ConfigureAwait(false);
        return Program.ExitScriptError;
    }

    private static string ReportName(string name) => FolderSlug.From(name) ?? "dataset";

    private static string Inv(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    /// <summary>The <c>orkeon run</c> options <c>eval run --crew</c> fills in code.</summary>
    private sealed class EvalCrewOptions : RunnerOptionsBase;
}
