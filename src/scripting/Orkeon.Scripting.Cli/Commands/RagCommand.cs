using Orkeon.Constants.FileSystem;
using System.Collections.Immutable;
using System.Globalization;
using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orkeon.Domain.FileSystem;
using Orkeon.Hosting;
using Orkeon.Rag.Abstractions;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.DependencyInjection;
using Orkeon.Rag.Evaluation;
using Orkeon.Rag.Onnx.DependencyInjection;
using Orkeon.Tools.Rag;
using Orkeon.Tools.Rag.DependencyInjection;

namespace Orkeon.Scripting.Cli.Commands;

/// <summary>Options shared by the <c>orkeon rag</c> verbs (host bootstrap surface).</summary>
internal abstract class RagCommandOptionsBase
{
    /// <summary>Path to appsettings.json (provides Llm/Embedding sections).</summary>
    [Option('s', "settings", Required = false,
        HelpText = "Path to appsettings.json (defaults to the current directory's resolution chain).")]
    public string? SettingsPath { get; set; }

    /// <summary>Mount strings in Docker-style format; several go space-separated after ONE flag.</summary>
    [Option('m', "mount", Required = false,
        HelpText = "File system mount(s) in Docker-style format: <physical>:<virtual>:<rights>. " +
                   "Several mounts go space-separated after ONE --mount (the flag cannot be repeated). " +
                   "The current directory is auto-mounted at /workspace (ro) and ./.orkeon at /output (rw) unless overridden.")]
    public IEnumerable<string> Mounts { get; set; } = [];

    /// <summary>Allow mounts whose base path is outside the cwd.</summary>
    [Option("allow-external-mounts", Required = false, Default = false,
        HelpText = "Allow mounts from directories outside the workspace root.")]
    public bool AllowExternalMounts { get; set; }

    /// <summary>Verbosity level 0-2 (aligned with the run verb).</summary>
    [Option('v', "verbose", Required = false, Default = 0,
        HelpText = "Verbosity level: 0=quiet, 1=info, 2=full debug.")]
    public int Verbose { get; set; }

    /// <summary>Effective opt-in: the flag OR the ORKEON_ALLOW_EXTERNAL_MOUNTS env variable.</summary>
    internal bool EffectiveAllowExternalMounts
        => AllowExternalMounts || RunnerEnvironment.AllowExternalMounts;

    /// <summary>Test seam: overrides <see cref="Directory.GetCurrentDirectory"/> for source resolution and default mounts.</summary>
    internal string? WorkingDirectoryOverride { get; set; }

    /// <summary>
    /// Test seam: services registered BEFORE <c>AddOrkeonRag</c>/<c>AddOrkeonRagTools</c>
    /// so hand-written doubles win the TryAdd race over the real pipelines.
    /// </summary>
    internal Action<HostBuilderContext, IServiceCollection>? ConfigureTestServices { get; set; }
}

/// <summary>Parsed CLI options for <c>orkeon rag ingest</c>.</summary>
[Verb("ingest", HelpText = "Ingest documents into a RAG collection (incremental: unchanged sources are skipped).")]
internal sealed class RagIngestCommandOptions : RagCommandOptionsBase
{
    /// <summary>Target collection.</summary>
    [Option('c', "collection", Required = true, HelpText = "Target collection in the document store.")]
    public string Collection { get; set; } = string.Empty;

    /// <summary>Source paths and/or glob patterns; several go space-separated after ONE flag.</summary>
    [Option("source", Required = true,
        HelpText = "Source path or glob pattern (e.g. \"./docs/**/*.md\"). Several sources go space-separated " +
                   "after ONE --source (the flag cannot be repeated). Globs are resolved through the virtual file system.")]
    public IEnumerable<string> Sources { get; set; } = [];

    /// <summary>Optional chunking strategy name.</summary>
    [Option("chunking", Required = false,
        HelpText = "Chunking strategy (recursive, sentence, structural, semantic). Pipeline default when omitted.")]
    public string? Chunking { get; set; }

    /// <summary>Force a full reindex of the collection.</summary>
    [Option("reindex", Required = false, Default = false,
        HelpText = "Force a full reindex of the collection (the only way past an embedding model/dimension change).")]
    public bool Reindex { get; set; }
}

/// <summary>Parsed CLI options for <c>orkeon rag search</c>.</summary>
[Verb("search", HelpText = "Ask a question against a RAG collection; prints the grounded answer with citations and scores.")]
internal sealed class RagSearchCommandOptions : RagCommandOptionsBase
{
    /// <summary>The question to ask.</summary>
    [Value(0, Required = true, MetaName = "question", HelpText = "The question to ask against the collection.")]
    public string Question { get; set; } = string.Empty;

    /// <summary>Collection to query.</summary>
    [Option('c', "collection", Required = true, HelpText = "Collection to query in the document store.")]
    public string Collection { get; set; } = string.Empty;

    /// <summary>Number of chunks kept for context assembly.</summary>
    [Option("top-n", Required = false, Default = 5,
        HelpText = "Number of relevant chunks kept for context assembly (default 5).")]
    public int TopN { get; set; } = 5;
}

/// <summary>Parsed CLI options for <c>orkeon rag eval</c>.</summary>
[Verb("eval", HelpText = "Evaluate a RAG collection against a golden dataset (recall@k, MRR, groundedness; judge mode labelled); writes markdown/JSON reports.")]
internal sealed class RagEvalCommandOptions : RagCommandOptionsBase
{
    /// <summary>Path to the golden dataset YAML file.</summary>
    [Option('d', "dataset", Required = true,
        HelpText = "Golden dataset YAML (e.g. examples/rag/eval/golden.yaml). Its 'corpus' directory is ingested first (incremental).")]
    public string Dataset { get; set; } = string.Empty;

    /// <summary>Collection override.</summary>
    [Option('c', "collection", Required = false,
        HelpText = "Collection to evaluate (default: the dataset's collection, else 'rag-eval-{dataset name}').")]
    public string? Collection { get; set; }

    /// <summary>Single profile to evaluate.</summary>
    [Option("profile", Required = false,
        HelpText = "Profile to evaluate: fast, balanced, quality, adaptive, corrective, or default (the configured Orkeon:Rag:Profile). Default: 'default'.")]
    public string? Profile { get; set; }

    /// <summary>Comma-separated list of profiles to compare.</summary>
    [Option("compare", Required = false,
        HelpText = "Comma-separated profiles to compare (e.g. fast,balanced,quality) — one report per profile plus a comparison table. Overrides --profile.")]
    public string? Compare { get; set; }

    /// <summary>Metric cutoff.</summary>
    [Option('k', Required = false, Default = 5,
        HelpText = "Cutoff of recall@k / precision@k (default 5).")]
    public int K { get; set; } = 5;

    /// <summary>Requests the LLM judge for generation metrics.</summary>
    [Option("llm-judge", Required = false, Default = false,
        HelpText = "Judge generation with the configured LLM; falls back to the deterministic heuristic (mode always labelled in the report).")]
    public bool LlmJudge { get; set; }

    /// <summary>Replaces generation with the deterministic extractive stub (CI/offline).</summary>
    [Option("offline", Required = false, Default = false,
        HelpText = "Zero-network run: generation is replaced by a deterministic extractive stub (retrieved passages verbatim), so no LLM key is needed.")]
    public bool Offline { get; set; }

    /// <summary>Skips the corpus ingestion pre-pass.</summary>
    [Option("no-ingest", Required = false, Default = false,
        HelpText = "Skip the corpus ingestion pre-pass (the collection must already be ingested).")]
    public bool NoIngest { get; set; }

    /// <summary>Forces a full corpus reindex before evaluating.</summary>
    [Option("reindex", Required = false, Default = false,
        HelpText = "Force a full corpus reindex. Required with the default in-memory store when ./.orkeon manifests survived a previous process, and after an embedding model change.")]
    public bool Reindex { get; set; }

    /// <summary>Anti-regression gate on recall@k.</summary>
    [Option("min-recall", Required = false,
        HelpText = "Anti-regression gate: exit 1 when the aggregate recall@k (cases tagged 'correctif' excluded) drops below this threshold.")]
    public double? MinRecall { get; set; }

    /// <summary>Anti-regression gate on MRR.</summary>
    [Option("min-mrr", Required = false,
        HelpText = "Anti-regression gate: exit 1 when the aggregate MRR (cases tagged 'correctif' excluded) drops below this threshold.")]
    public double? MinMrr { get; set; }

    /// <summary>Virtual output directory of the reports.</summary>
    [Option("output", Required = false, Default = "/output/rag/eval",
        HelpText = "Virtual directory receiving the markdown/JSON reports (default /output/rag/eval → ./.orkeon/rag/eval).")]
    public string Output { get; set; } = "/output/rag/eval";
}

/// <summary>
/// <c>orkeon rag ingest | search | eval</c> — ingestion, retrieval and evaluation
/// surfaces of the RAG subsystem (RAG-03/C3, RAG-04/C1). Builds the same
/// shared host as the <c>run</c> verb (<see cref="RunnerHost.Build"/>: settings, VFS
/// mounts, LLM/embedding wiring) and additionally registers the RAG subsystem
/// (<c>AddOrkeonRag</c>) plus its agent tools (<c>AddOrkeonRagTools</c>). Relative
/// source paths resolve against an automatic <c>{cwd} → /workspace:ro</c> mount;
/// ingestion state (manifests, default <c>/output/rag/manifests</c>) lands in an
/// automatic <c>{cwd}/.orkeon → /output:rw</c> mount.
/// </summary>
internal static class RagCommand
{
    /// <summary>Parses <paramref name="args"/> (already stripped of the leading <c>rag</c>) and dispatches to a verb.</summary>
    public static async Task<int> DispatchAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        using var parser = new Parser(s =>
        {
            s.HelpWriter = Console.Out;
            s.CaseInsensitiveEnumValues = true;
        });

        return await parser.ParseArguments<RagIngestCommandOptions, RagSearchCommandOptions, RagEvalCommandOptions>(args)
            .MapResult(
                (RagIngestCommandOptions o) => ExecuteIngestAsync(o),
                (RagSearchCommandOptions o) => ExecuteSearchAsync(o),
                (RagEvalCommandOptions o) => ExecuteEvalAsync(o),
                _ => Task.FromResult(Program.ExitScriptError))
            .ConfigureAwait(false);
    }

    /// <summary>Runs the ingestion described by <paramref name="options"/>; returns the CLI exit code.</summary>
    public static Task<int> ExecuteIngestAsync(RagIngestCommandOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return GuardedAsync("ingest", async ct =>
        {
            var cwd = ResolveCwd(options);
            using var host = BuildHost(options, cwd);

            var fileSystem = host.Services.GetRequiredService<IFileSystemService>();
            var mounts = fileSystem.GetAvailableMounts();
            var patterns = options.Sources
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => ToVirtualSource(s, cwd, mounts))
                .ToList();
            if (patterns.Count == 0)
            {
                await Console.Error.WriteLineAsync("orkeon rag ingest: at least one --source is required.").ConfigureAwait(false);
                return Program.ExitScriptError;
            }

            var sources = await SourceGlobExpander.ExpandAsync(fileSystem, patterns, ct).ConfigureAwait(false);
            if (sources.Count == 0)
            {
                await Console.Error.WriteLineAsync(
                    $"orkeon rag ingest: no source matched: {string.Join(", ", options.Sources)}").ConfigureAwait(false);
                return Program.ExitScriptError;
            }

            var pipeline = host.Services.GetRequiredService<IIngestionPipeline>();
            var report = await pipeline.IngestAsync(new IngestionRequest
            {
                Collection = options.Collection,
                Sources = [.. sources],
                ChunkingStrategy = string.IsNullOrWhiteSpace(options.Chunking) ? null : options.Chunking,
                Reindex = options.Reindex,
            }, ct).ConfigureAwait(false);

            // Same report text as the rag_ingest agent tool — one format for both surfaces.
            Console.WriteLine(RagIngestTool.FormatReport(report));
            return Program.ExitOk;
        });
    }

    /// <summary>Runs the query described by <paramref name="options"/>; returns the CLI exit code.</summary>
    public static Task<int> ExecuteSearchAsync(RagSearchCommandOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return GuardedAsync("search", async ct =>
        {
            if (string.IsNullOrWhiteSpace(options.Question))
            {
                await Console.Error.WriteLineAsync("orkeon rag search: a question is required.").ConfigureAwait(false);
                return Program.ExitScriptError;
            }

            var cwd = ResolveCwd(options);
            using var host = BuildHost(options, cwd);

            var pipeline = host.Services.GetRequiredService<IRagPipeline>();
            var answer = await pipeline.QueryAsync(new RagQuery
            {
                Text = options.Question,
                Collection = options.Collection,
                TopN = options.TopN,
            }, ct).ConfigureAwait(false);

            Console.WriteLine(answer.Text);
            if (answer.Citations.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Sources:");
                foreach (var citation in answer.Citations)
                {
                    var snippet = citation.Snippet ?? string.Empty;
                    var preview = snippet.Length > 100 ? snippet[..100] + "..." : snippet;
                    Console.WriteLine(string.Create(
                        CultureInfo.InvariantCulture,
                        $"- [{citation.Marker}] {citation.SourceId} (score: {citation.Score:F2}): {preview}"));
                }
            }

            return Program.ExitOk;
        });
    }

    /// <summary>
    /// Runs the evaluation described by <paramref name="options"/>; returns the CLI
    /// exit code. The harness ingests the dataset's corpus first (incremental),
    /// evaluates each requested profile, prints the same summary as the
    /// <c>rag_eval</c> agent tool, then applies the optional anti-regression gates
    /// (<c>--min-recall</c>/<c>--min-mrr</c>, cases tagged
    /// '<see cref="Orkeon.Rag.Abstractions.Models.RagEvalCase.CorrectiveTag"/>' excluded).
    /// </summary>
    public static Task<int> ExecuteEvalAsync(RagEvalCommandOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return GuardedAsync("eval", async ct =>
        {
            if (string.IsNullOrWhiteSpace(options.Dataset))
            {
                await Console.Error.WriteLineAsync("orkeon rag eval: --dataset is required.").ConfigureAwait(false);
                return Program.ExitScriptError;
            }

            // Offline mode: a deterministic extractive IChatClient wins over the
            // TryAdd/last-wins defaults so the whole run needs zero network. It is
            // registered before the caller's test seam so hand-written doubles keep
            // the last word.
            if (options.Offline)
            {
                var userSeam = options.ConfigureTestServices;
                options.ConfigureTestServices = (ctx, services) =>
                {
                    services.AddSingleton<Microsoft.Extensions.AI.IChatClient>(
                        new Orkeon.Rag.Evaluation.ExtractiveOfflineChatClient());
                    userSeam?.Invoke(ctx, services);
                };
            }

            var cwd = ResolveCwd(options);
            using var host = BuildHost(options, cwd);

            var fileSystem = host.Services.GetRequiredService<IFileSystemService>();
            var datasetPath = ToVirtualSource(options.Dataset, cwd, fileSystem.GetAvailableMounts());

            // --compare wins over --profile; neither given means "the default pipeline".
            var singleProfile = string.IsNullOrWhiteSpace(options.Profile)
                ? ImmutableList<string>.Empty
                : ImmutableList.Create(options.Profile!);

            var profiles = string.IsNullOrWhiteSpace(options.Compare)
                ? singleProfile
                : options.Compare!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToImmutableList();

            var harness = host.Services.GetRequiredService<IRagEvalHarness>();
            var result = await harness.RunAsync(new RagEvalRunRequest
            {
                DatasetPath = datasetPath,
                Profiles = profiles,
                Collection = string.IsNullOrWhiteSpace(options.Collection) ? null : options.Collection,
                K = options.K,
                UseLlmJudge = options.LlmJudge,
                IngestCorpus = !options.NoIngest,
                ReindexCorpus = options.Reindex,
                OutputDirectory = options.Output,
            }, ct).ConfigureAwait(false);

            // Same summary text as the rag_eval agent tool — one format for both surfaces.
            Console.WriteLine(RagEvalTool.FormatResult(result));

            return await ApplyGatesAsync(options, result).ConfigureAwait(false);
        });
    }

    /// <summary>
    /// Applies the anti-regression gates to every evaluated profile: aggregate
    /// recall@k / MRR computed WITHOUT the cases tagged
    /// '<see cref="Orkeon.Rag.Abstractions.Models.RagEvalCase.CorrectiveTag"/>'
    /// (those are seeded to fail plain retrieval — RAG-06 material). Any profile
    /// below a threshold fails the run with exit code 1.
    /// </summary>
    private static async Task<int> ApplyGatesAsync(RagEvalCommandOptions options, RagEvalRunResult result)
    {
        if (options.MinRecall is null && options.MinMrr is null)
            return Program.ExitOk;

        var failed = false;
        foreach (var report in result.Reports)
        {
            var gatedRecall = RagEvalGate.MeanRecallExcluding(report, RagEvalCase.CorrectiveTag);
            var gatedMrr = RagEvalGate.MeanReciprocalRankExcluding(report, RagEvalCase.CorrectiveTag);

            if (options.MinRecall is { } minRecall && (gatedRecall is null || gatedRecall < minRecall))
            {
                failed = true;
                await Console.Error.WriteLineAsync(string.Create(
                    CultureInfo.InvariantCulture,
                    $"orkeon rag eval: REGRESSION — profile '{report.Profile}' recall@{report.K} = {gatedRecall ?? 0:F3} < threshold {minRecall:F3} (cases tagged '{RagEvalCase.CorrectiveTag}' excluded).")).ConfigureAwait(false);
            }

            if (options.MinMrr is { } minMrr && (gatedMrr is null || gatedMrr < minMrr))
            {
                failed = true;
                await Console.Error.WriteLineAsync(string.Create(
                    CultureInfo.InvariantCulture,
                    $"orkeon rag eval: REGRESSION — profile '{report.Profile}' MRR = {gatedMrr ?? 0:F3} < threshold {minMrr:F3} (cases tagged '{RagEvalCase.CorrectiveTag}' excluded).")).ConfigureAwait(false);
            }
        }

        return failed ? Program.ExitScriptError : Program.ExitOk;
    }

    /// <summary>
    /// Top-level fault barrier shared by both verbs: Ctrl+C → 130, configuration /
    /// input errors (missing files, unknown chunker, embedding-profile drift) → 1,
    /// anything unexpected → 2. Mirrors the exit-code contract of the run verb.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Top-level CLI fault barrier: unexpected failures are converted to a runtime-error exit code so the tool reports cleanly instead of crashing with a stack trace.")]
    private static async Task<int> GuardedAsync(string verb, Func<CancellationToken, Task<int>> body)
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            return await body(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Program.ExitCancelled;
        }
        catch (Exception ex) when (ex is ArgumentException
            or InvalidOperationException
            or FileNotFoundException
            or DirectoryNotFoundException
            or FileAccessDeniedException)
        {
            var root = Orkeon.Scripting.Internal.JsExceptionUnwrap.UnwrapToInnermost(ex);
            await Console.Error.WriteLineAsync($"orkeon rag {verb}: {root.Message}").ConfigureAwait(false);
            return Program.ExitScriptError;
        }
        catch (Exception ex)
        {
            var root = Orkeon.Scripting.Internal.JsExceptionUnwrap.UnwrapToInnermost(ex);
            await Console.Error.WriteLineAsync(
                $"orkeon rag {verb}: unexpected error [{root.GetType().FullName}]: {root.Message}").ConfigureAwait(false);
            if (Environment.GetEnvironmentVariable("ORKEON_DEBUG") == "1")
                await Console.Error.WriteLineAsync(ex.ToString()).ConfigureAwait(false);
            return Program.ExitRuntimeError;
        }
    }

    private static string ResolveCwd(RagCommandOptionsBase options)
        => Path.GetFullPath(options.WorkingDirectoryOverride ?? Directory.GetCurrentDirectory());

    /// <summary>
    /// Builds the shared runner host with the RAG subsystem on top: default mounts
    /// (<c>{cwd} → /workspace:ro</c> for corpus reads, <c>{cwd}/.orkeon → /output:rw</c>
    /// for ingestion manifests) are injected unless the caller already claimed those
    /// virtual roots via <c>--mount</c>.
    /// </summary>
    private static IHost BuildHost(RagCommandOptionsBase options, string cwd)
    {
        var settingsPath = RunnerSettings.ResolveSettingsPath(options.SettingsPath, cwd);
        if (settingsPath != null)
            Console.Error.WriteLine($"Using settings: {settingsPath}");

        var cliMounts = options.Mounts.Where(m => !string.IsNullOrWhiteSpace(m)).ToList();

        // The settings file declares mounts too, and RunnerHost appends ours after them: a
        // declared /workspace or /output met an injected twin and the host died on
        // "Duplicate virtual paths" out of a DI factory. Yielding to whoever already claimed
        // the root is this command's existing policy for --mount; it just never saw the other
        // half of the list it was reasoning about.
        var claimed = cliMounts.Concat(RunnerSettings.ReadDeclaredMounts(settingsPath)).ToList();

        if (!claimed.Any(m => ClaimsVirtualRoot(m, "/workspace")))
            cliMounts.Insert(0, $"{FileSystemMount.Quote(cwd)}:/workspace:ro");

        if (!claimed.Any(m => ClaimsVirtualRoot(m, "/output")))
        {
            var stateDir = Path.Combine(cwd, ConventionalNames.StateDirectory);
            // EXCEPTION-BOOTSTRAP: provisions the manifest mount's physical directory
            // before the DI container (and thus IFileSystemService) exists.
            Directory.CreateDirectory(stateDir);
            cliMounts.Add($"{FileSystemMount.Quote(stateDir)}:/output:rw");
        }

        var verbosity = Math.Clamp(options.Verbose, 0, 2);

        // The corpus root is the verb's primary input (parity with the run verb's
        // /script mount): when it lives outside the process cwd (tests, `--mount`
        // scenarios), it must be whitelisted regardless of --allow-external-mounts.
        var processCwd = Directory.GetCurrentDirectory();
        // Containment, not spelling — see RunCommand: ~/proj-old is not inside ~/proj.
        var implicitlyAllow = options.EffectiveAllowExternalMounts
            || !Orkeon.Domain.FileSystem.PhysicalPathContainment.IsUnder(cwd, processCwd);

        return RunnerHost.Build(
            settingsPath,
            cliMounts,
            allowExternalMounts: implicitlyAllow,
            llmLogVirtualPath: null,
            configureLogging: verbosity > 0
                ? (_, b) => RunnerExecution.ConfigureVerboseLogging(b, verbosity)
                : null,
            configureServices: (ctx, services) =>
            {
                // Test doubles first: AddOrkeonRag uses TryAdd*, so a pre-registered
                // IIngestionPipeline/IRagPipeline wins over the real pipelines.
                options.ConfigureTestServices?.Invoke(ctx, services);
                services.AddOrkeonRag(ctx.Configuration);
                // ONNX cross-encoder (embedded weights via Orkeon.Rag.Onnx.Model):
                // required by the balanced/quality profiles, loaded lazily at
                // first use — profiles that never rerank pay nothing.
                services.AddOrkeonOnnxReranker();
                services.AddOrkeonRagTools();
            });
    }

    /// <summary>True when the Docker-style mount string claims <paramref name="virtualRoot"/>.</summary>
    internal static bool ClaimsVirtualRoot(string mount, string virtualRoot)
    {
        // Ask the grammar, never the substring: a physical path may legally carry ':' when
        // quoted ("/mnt/x:/output:y":/corpus:ro), and reading the spec by hand would see an
        // /output claim that is not there. A malformed spec claims nothing — the parser
        // reports it, with its own message, when the host is built.
        try
        {
            return string.Equals(
                FileSystemMount.Parse(mount).VirtualPath.TrimEnd('/'),
                virtualRoot.TrimEnd('/'),
                StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Maps a user-supplied source (relative path, absolute path under the cwd, or an
    /// already-virtual path) onto a virtual path/glob resolvable by the VFS. Anything
    /// outside every mount is refused with an actionable message.
    /// </summary>
    internal static string ToVirtualSource(string raw, string cwd, IReadOnlyList<MountInfo> mounts)
    {
        var pattern = raw.Replace('\\', '/');
        if (pattern.StartsWith("./", StringComparison.Ordinal))
            pattern = pattern[2..];

        // Already virtual: matches one of the active mounts' virtual prefixes.
        foreach (var mount in mounts)
        {
            var root = mount.VirtualPath.TrimEnd('/');
            if (pattern.Equals(root, StringComparison.Ordinal)
                || pattern.StartsWith(root + "/", StringComparison.Ordinal))
            {
                return pattern;
            }
        }

        // Absolute physical path under the cwd → rebase onto the /workspace mount.
        var cwdNorm = cwd.Replace('\\', '/').TrimEnd('/');
        if (pattern.StartsWith(cwdNorm + "/", StringComparison.Ordinal))
            return "/workspace/" + pattern[(cwdNorm.Length + 1)..];

        if (Path.IsPathRooted(raw))
        {
            throw new ArgumentException(
                $"source '{raw}' is outside the current directory. Run from the corpus root, " +
                "or mount it explicitly (--mount <dir>:<virtual>:ro --allow-external-mounts) and pass its virtual path.");
        }

        return "/workspace/" + pattern.TrimStart('/');
    }
}
