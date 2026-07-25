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

    /// <summary>Repeatable mount strings in Docker-style format.</summary>
    [Option('m', "mount", Required = false,
        HelpText = "File system mount(s) in Docker-style format: <physical>:<virtual>:<rights>. Repeatable. " +
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

    /// <summary>Repeatable source paths and/or glob patterns.</summary>
    [Option("source", Required = true,
        HelpText = "Source path or glob pattern (e.g. \"./docs/**/*.md\"). Repeatable. Globs are resolved through the virtual file system.")]
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

/// <summary>
/// <c>orkeon rag ingest | search</c> — ingestion and retrieval surfaces of the RAG
/// subsystem (RAG-03/C3; <c>orkeon rag eval</c> arrives with RAG-04). Builds the same
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

        return await parser.ParseArguments<RagIngestCommandOptions, RagSearchCommandOptions>(args)
            .MapResult(
                (RagIngestCommandOptions o) => ExecuteIngestAsync(o),
                (RagSearchCommandOptions o) => ExecuteSearchAsync(o),
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

        if (!cliMounts.Any(m => ClaimsVirtualRoot(m, "/workspace")))
            cliMounts.Insert(0, $"{cwd}:/workspace:ro");

        if (!cliMounts.Any(m => ClaimsVirtualRoot(m, "/output")))
        {
            var stateDir = Path.Combine(cwd, ".orkeon");
            // EXCEPTION-BOOTSTRAP: provisions the manifest mount's physical directory
            // before the DI container (and thus IFileSystemService) exists.
            Directory.CreateDirectory(stateDir);
            cliMounts.Add($"{stateDir}:/output:rw");
        }

        var verbosity = Math.Clamp(options.Verbose, 0, 2);

        // The corpus root is the verb's primary input (parity with the run verb's
        // /script mount): when it lives outside the process cwd (tests, `--mount`
        // scenarios), it must be whitelisted regardless of --allow-external-mounts.
        var processCwd = Directory.GetCurrentDirectory();
        var implicitlyAllow = options.EffectiveAllowExternalMounts
            || !cwd.StartsWith(processCwd, StringComparison.Ordinal);

        return RunnerHost.Build(
            settingsPath,
            cliMounts,
            allowExternalMounts: implicitlyAllow,
            llmLogPath: null,
            configureLogging: verbosity > 0
                ? (_, b) => RunnerExecution.ConfigureVerboseLogging(b, verbosity)
                : null,
            configureServices: (ctx, services) =>
            {
                // Test doubles first: AddOrkeonRag uses TryAdd*, so a pre-registered
                // IIngestionPipeline/IRagPipeline wins over the real pipelines.
                options.ConfigureTestServices?.Invoke(ctx, services);
                services.AddOrkeonRag(ctx.Configuration);
                services.AddOrkeonRagTools();
            });
    }

    /// <summary>True when the Docker-style mount string claims <paramref name="virtualRoot"/>.</summary>
    internal static bool ClaimsVirtualRoot(string mount, string virtualRoot)
    {
        // <physical>:<virtual>[:<rights>] — match the virtual segment exactly
        // (":/output" must not match ":/output-archive").
        return mount.Contains($":{virtualRoot}:", StringComparison.Ordinal)
            || mount.EndsWith($":{virtualRoot}", StringComparison.Ordinal);
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
