using System.Text.Json;
using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Crew;
using Orkeon.Application.EventHub;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.Tools;
using Orkeon.Hosting;
using Orkeon.Rag.DependencyInjection;
using Orkeon.Rag.Onnx.DependencyInjection;
using Orkeon.Scripting.Configuration;
using Orkeon.Tools.Rag.DependencyInjection;
using Orkeon.Scripting.Internal;
using Orkeon.Scripting.Toolchain;

namespace Orkeon.Scripting.Cli.Commands;

/// <summary>Parsed CLI options for the <c>run</c> verb.</summary>
internal sealed class RunCommandOptions
{
    /// <summary>
    /// Path to the crew definition: a script (.ork.ts/.js), a YAML crew (.yaml/.yml), or a
    /// directory holding a multi-file YAML crew (config.yaml + agents/ + tasks/).
    /// </summary>
    [Value(0, Required = false,
        HelpText = "Path to the crew definition: .ork.ts/.js (Scripting DSL), .yaml/.yml (YAML crew), " +
                   "or a directory holding a multi-file YAML crew (config.yaml + agents/ + tasks/). " +
                   "Required unless --list-tools.")]
    public string ScriptPath { get; set; } = string.Empty;

        /// <summary>Path to appsettings.json (provides Llm section + RaggableTree).</summary>
        [Option('s', "settings", Required = false,
            HelpText = "Path to appsettings.json (defaults to same dir as script).")]
        public string? SettingsPath { get; set; }

        /// <summary>Repeatable mount strings in Docker-style format.</summary>
        [Option('m', "mount", Required = false,
            HelpText = "File system mount(s) in Docker-style format: <physical>:<virtual>:<rights>[;sub:rights]. Repeatable.")]
        public IEnumerable<string> Mounts { get; set; } = [];

        /// <summary>Allow mounts whose base path is outside the cwd.</summary>
        [Option("allow-external-mounts", Required = false, Default = false,
            HelpText = "Allow mounts from directories outside the workspace root. Mount base paths are added to the security whitelist. " +
                       "Can also be enabled for every invocation via ORKEON_ALLOW_EXTERNAL_MOUNTS=1.")]
        public bool AllowExternalMounts { get; set; }

        /// <summary>
        /// Effective opt-in: the <c>--allow-external-mounts</c> flag OR the
        /// <c>ORKEON_ALLOW_EXTERNAL_MOUNTS</c> environment variable (see <see cref="RunnerEnvironment"/>).
        /// </summary>
        internal bool EffectiveAllowExternalMounts
            => AllowExternalMounts || RunnerEnvironment.AllowExternalMounts;

        /// <summary>Verbosity level 0-2 (aligned with YAML runner).</summary>
        [Option('v', "verbose", Required = false, Default = 0,
            HelpText = "Verbosity level: 0=quiet, 1=LLM & tool exchanges, 2=full debug.")]
        public int Verbose { get; set; }

        /// <summary>Enable LLM exchange logging to JSONL files.</summary>
        [Option("llm-log", Required = false, Default = false,
            HelpText = "Enable LLM exchange logging (writes JSONL to ./llm-logs unless --llm-log-path overrides).")]
        public bool LlmLogEnabled { get; set; }

        /// <summary>Custom directory for LLM exchange logs (implies --llm-log).</summary>
        [Option("llm-log-path", Required = false, Default = null,
            HelpText = "Directory for LLM exchange log files (.jsonl). Implies --llm-log.")]
        public string? LlmLogPath { get; set; }

        /// <summary>Inline JSON inputs forwarded to the script (assigned as global <c>inputs</c>).</summary>
        [Option("inputs", HelpText = "Inline JSON inputs forwarded as a global `inputs` variable.")]
        public string? InputsJson { get; set; }

        /// <summary>Path to a JSON file holding the inputs.</summary>
        [Option("inputs-file", HelpText = "Path to a JSON file holding the inputs.")]
        public string? InputsFilePath { get; set; }

        /// <summary>
        /// Override the Jint memory limit for this run. Useful when a script
        /// bundles large knowledge corpora or large prompt fixtures and the
        /// default ceiling is too tight. Set to 0 to disable the limit
        /// (use with care — runaway scripts will then OOM the host).
        /// </summary>
        [Option("memory-limit-mb", Required = false,
            HelpText = "Jint memory limit in megabytes for this run (overrides appsettings). Set 0 to disable; default comes from Orkeon:Scripting:Limits:MemoryLimitBytes.")]
        public long? MemoryLimitMb { get; set; }

        /// <summary>
        /// Repeatable <c>KEY=VALUE</c> variables forwarded to <c>CrewInput</c> — YAML crews only.
        /// Mirrors the standard runner's <c>-V/--var</c>; ignored on the script (.ork.ts) path,
        /// which receives structured inputs via <c>--inputs</c>/<c>--inputs-file</c> instead.
        /// </summary>
        [Option('V', "var", Required = false,
            HelpText = "Variable for a YAML crew's CrewInput (KEY=VALUE). Repeatable. Used by task templates: {KEY} → VALUE. Ignored for .ork.ts scripts.")]
        public IEnumerable<string> Variables { get; set; } = [];

        /// <summary>Initial context string passed to <c>CrewInput</c> — YAML crews only.</summary>
        [Option("initial-context", Required = false, Default = null,
            HelpText = "Initial context string passed to a YAML crew's CrewInput. Ignored for .ork.ts scripts.")]
        public string? InitialContext { get; set; }

        /// <summary>
        /// Emits the versioned event protocol on stdout instead of plain text (BUS-02).
        /// The value is accepted for the spec's spelling (<c>--events jsonl</c>) and for
        /// forward compatibility; <c>jsonl</c> is the only stream format there is.
        /// </summary>
        [Option("events", Required = false, Default = null,
            HelpText = "Emit the versioned JSONL event protocol on stdout (task progress, cost, " +
                       "run outcome) instead of plain text. This is how Orkeon Studio drives a run.")]
        public string? Events { get; set; }

        /// <summary>
        /// Includes token-by-token <c>llm.delta</c> events in the stream. Opt-in: a delta per
        /// token saturates both the pipe and any UI reading it.
        /// </summary>
        [Option("stream", Required = false, Default = false,
            HelpText = "With --events, also emit llm.delta events token by token. Verbose by " +
                       "nature: off unless asked for.")]
        public bool Stream { get; set; }

        /// <summary>
        /// The name the observing process answers to, as it appears in <c>client://{name}</c>.
        /// Agents post to that address to reach it, and a crew authorizes the exchange by
        /// declaring <c>to: "client:{name}"</c> in its <c>links:</c> block.
        /// </summary>
        [Option("client", Required = false, Default = "studio",
            HelpText = "With --events, the name of the observing peer on the hub (client://<name>). " +
                       "Agents can post and send to that address; a crew's links: block authorizes it.")]
        public string Client { get; set; } = "studio";

        /// <summary>Whether the run must emit the event protocol.</summary>
        internal bool EmitsEvents => Events is not null;

        /// <summary>
        /// Dry-run: build the host and load the crew (strict tool resolution) without probing the
        /// LLM endpoint or running a kickoff. Mirrors the standard runner's <c>--validate</c>.
        /// </summary>
        [Option("validate", Required = false, Default = false,
            HelpText = "Dry-run: resolve settings, build the host and load the crew (strict tool " +
                       "resolution) WITHOUT probing the LLM endpoint or running a kickoff. Prints " +
                       "'VALIDATION OK/FAILED: <config> ...' and exits 0 (ok) or non-zero (failed).")]
        public bool Validate { get; set; }

        /// <summary>
        /// Build the host and print the sorted runtime tool registry (one name per line), then exit.
        /// Mirrors the standard runner's <c>--list-tools</c>; no crew is loaded, so the crew path
        /// is not required.
        /// </summary>
        [Option("list-tools", Required = false, Default = false,
            HelpText = "Build the host and print the sorted list of registered tool names (one per " +
                       "line) to stdout, then exit 0. No crew is loaded, so the crew path is not " +
                       "required.")]
        public bool ListTools { get; set; }

        /// <summary>Computed log path or null when logging is disabled.</summary>
        internal string? ResolvedLlmLogPath
        {
            get
            {
                if (!LlmLogEnabled && string.IsNullOrWhiteSpace(LlmLogPath)) return null;
                var dir = string.IsNullOrWhiteSpace(LlmLogPath) ? "llm-logs" : LlmLogPath;
                return Path.GetFullPath(dir);
            }
        }
}

/// <summary>
/// <c>orkeon run &lt;crew.ork.ts | crew.yaml | crew-dir/&gt;</c> — runs a crew definition and emits
/// its result on stdout. A directory target is classified by its layout
/// (<see cref="CrewDirectoryLayout"/>); everything else dispatches by file extension:
/// <list type="bullet">
///   <item><description>a directory holding a multi-file YAML crew (<c>config.yaml</c> +
///   <c>agents/</c> + <c>tasks/</c>, or the flat <c>crew.yaml</c>/<c>agents.yaml</c>/<c>tasks.yaml</c>
///   triplet) → the shared one-shot YAML runner, same as a single YAML file.</description></item>
///   <item><description><c>.yaml</c>/<c>.yml</c> → the shared one-shot YAML runner
///   (<see cref="RunnerExecution.RunOneShotAsync"/>), identical to the standalone
///   standard runner — loads the crew, kicks it off, prints the crew output.</description></item>
///   <item><description><c>.ork.ts</c>/<c>.js</c> → the scripting host (esbuild + Jint),
///   which evaluates the script and emits its <c>result</c> value as JSON.</description></item>
/// </list>
/// Both paths share the same host bootstrap surface: <c>--settings</c>, <c>--mount</c>,
/// <c>--allow-external-mounts</c>, <c>--llm-log[-path]</c>, <c>--verbose</c>.
/// </summary>
internal static partial class RunCommand
{
    /// <summary>Concrete <see cref="RunnerOptionsBase"/> built from <see cref="RunCommandOptions"/> for the YAML path.</summary>
    private sealed class YamlRunnerOptions : RunnerOptionsBase;

    /// <summary>Serialization options for the primary result payload (relaxed escaping).</summary>
    private static readonly JsonSerializerOptions ResultJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Serialization options for the ToString() fallback payload.</summary>
    private static readonly JsonSerializerOptions FallbackJsonOptions = new()
    {
        WriteIndented = true,
    };

    /// <summary>Loads the script and executes it; returns the CLI exit code.</summary>
    public static Task<int> ExecuteAsync(RunCommandOptions options)
    {
        // Validate eagerly (synchronously) so a null argument surfaces at the call
        // site rather than being captured inside the returned Task (S4457).
        ArgumentNullException.ThrowIfNull(options);
        return ExecuteCoreAsync(options);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Top-level CLI fault barrier: after cancellation, file-not-found and esbuild errors are handled specifically, any other unexpected failure is converted to a runtime-error exit code so the tool reports cleanly instead of crashing with a stack trace.")]
    private static async Task<int> ExecuteCoreAsync(RunCommandOptions options)
    {
        // --list-tools dumps the runtime tool registry and needs no crew definition: it goes
        // straight to the shared runner (same host, same tool set as a real kickoff), so the
        // emitted manifest matches the standard runner byte-for-byte. Handled BEFORE the
        // config-path checks precisely because it must run without a path.
        if (options.ListTools)
            return await RunViaSharedRunnerAsync(options).ConfigureAwait(false);

        // Every remaining mode (validate or run) needs a crew definition path.
        if (string.IsNullOrWhiteSpace(options.ScriptPath))
        {
            await Console.Error.WriteLineAsync(
                "orkeon run: a crew definition path is required (unless --list-tools).").ConfigureAwait(false);
            return Program.ExitScriptError;
        }

        // A crew can also be a DIRECTORY holding a multi-file YAML definition (config.yaml +
        // agents/ + tasks/, or the flat legacy triplet). Classify it before every other branch —
        // including --validate — so an ambiguous or layout-less directory reports the layout
        // diagnostic here instead of falling through to an extension test that cannot describe it.
        // OUT-OF-SCOPE: probing the user-supplied crew path; CLI entry runs outside the VFS
        // abstraction (crews live wherever the user invokes us from).
        if (Directory.Exists(options.ScriptPath))
        {
            var inspection = CrewDirectoryLayout.Inspect(options.ScriptPath);
            if (!inspection.IsCrewDirectory)
            {
                await Console.Error.WriteLineAsync($"orkeon run: {inspection.Error}").ConfigureAwait(false);
                return Program.ExitScriptError;
            }

            // Same shared one-shot runner as a .yaml crew — it owns --validate, the mounts and
            // the exit codes, so every option behaves identically on a directory and on a file.
            return await RunViaSharedRunnerAsync(options).ConfigureAwait(false);
        }

        // --validate is a crew-load concern (YAML or .ork.ts crew definition): the shared runner
        // loads the crew strictly and never probes the LLM, dispatching on the config extension.
        if (options.Validate)
            return await RunViaSharedRunnerAsync(options).ConfigureAwait(false);

        // Dispatch by extension BEFORE any script-specific setup (esbuild, /script:ro mount).
        // YAML crews delegate entirely to the shared one-shot runner — the same code path the
        // standalone standard runner uses — so a single published `orkeon` tool runs both
        // crew.ork.ts and crew.yaml without the consumer having to compile a runner.
        if (IsYamlConfig(options.ScriptPath))
            return await RunViaSharedRunnerAsync(options).ConfigureAwait(false);

        // OUT-OF-SCOPE: probing the user-supplied script path; CLI entry runs outside
        // the VFS abstraction (scripts live wherever the user invokes us from).
        if (!File.Exists(options.ScriptPath))
        {
            await Console.Error.WriteLineAsync($"orkeon run: script not found: {options.ScriptPath}").ConfigureAwait(false);
            return Program.ExitScriptError;
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true; // let the script wind down rather than crash.
            cts.Cancel();
        };

        try
        {
            return await RunWithHostAsync(options, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Program.ExitCancelled;
        }
        catch (FileNotFoundException ex)
        {
            await Console.Error.WriteLineAsync($"orkeon run: {ex.Message}").ConfigureAwait(false);
            return Program.ExitScriptError;
        }
        catch (EsbuildNotFoundException ex)
        {
            return ReportEsbuildNotFound(ex);
        }
        catch (EsbuildTranspileException ex)
        {
            return ReportEsbuildTranspileError(ex);
        }
        catch (Exception ex)
        {
            return ReportUnexpectedError(ex);
        }
    }

    /// <summary>True when the config path is a YAML crew (<c>.yaml</c>/<c>.yml</c>, case-insensitive).</summary>
    private static bool IsYamlConfig(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".yaml", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".yml", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Delegates to the shared one-shot runner (<see cref="RunnerExecution.RunOneShotAsync"/>) — the
    /// same code path the standalone standard runner used. Registers the <c>semantic_search</c> tool
    /// (via <see cref="SemanticSearchToolExtensions.AddSemanticSearchTool"/>) so YAML crews that list
    /// it resolve it, and so <c>--list-tools</c> reports the full runtime manifest. The runner
    /// short-circuits on <c>--list-tools</c> (no crew loaded) and <c>--validate</c> (strict crew load,
    /// no LLM probe / no kickoff), and otherwise runs the crew end-to-end. It owns the file-existence
    /// check, SIGINT/SIGTERM graceful shutdown, mount auto-injection and exit codes (0/1/2/130) —
    /// which already coincide with
    /// <see cref="Program.ExitOk"/>/<see cref="Program.ExitScriptError"/>/<see cref="Program.ExitRuntimeError"/>/<see cref="Program.ExitCancelled"/>,
    /// so we return its code verbatim.
    /// </summary>
    private static async Task<int> RunViaSharedRunnerAsync(RunCommandOptions options)
    {
        if (!options.EmitsEvents)
        {
            return await RunnerExecution.RunOneShotAsync(
                ToRunnerOptions(options),
                "orkeon",
                configureServices: (_, services) => services.AddSemanticSearchTool())
                .ConfigureAwait(false);
        }

        return await RunWithEventsAsync(options).ConfigureAwait(false);
    }

    /// <summary>
    /// The observed run (BUS-02): the same shared runner, with the event stream wired onto
    /// the seams that already exist — task completions, the token meter, and generation
    /// deltas under <c>--stream</c>. The stream opens before the host is built and closes
    /// on the runner's own exit code, so a configuration failure is reported as an event
    /// rather than as silence.
    /// </summary>
    private static async Task<int> RunWithEventsAsync(RunCommandOptions options)
    {
        var events = new Events.OrkeonEventWriter(Console.Out);
        events.Emit(Run.RunEventKinds.RunStarted, new
        {
            target = options.ScriptPath,
            stream = options.Stream,
        });

        Run.RunEventObserver? observer = null;
        Run.JsonLinesEventHubBridge? bridge = null;

        // One reader on stdin, routed by kind. Two would race, and BUS-04's channel dropped
        // every line that was not a human answer — including the hub commands.
        var inbound = new Run.InboundCommandPump(
            Console.In,
            (line, ct) => bridge?.HandleCommandAsync(line, ct) ?? System.Threading.Tasks.Task.CompletedTask);
        await using var inboundLifetime = inbound.ConfigureAwait(false);

        var exitCode = await RunnerExecution.RunOneShotAsync(
            ToRunnerOptions(options),
            "orkeon",
            configureServices: (_, services) =>
            {
                services.AddSemanticSearchTool();

                // ICrewExecutionHook is a single service and the runner may already have
                // registered AutoSummaryWriter on it. Take that registration over rather
                // than past it: observing a run must not cost it its AUTO_SUMMARY.md.
                var existing = services.LastOrDefault(d => d.ServiceType == typeof(ICrewExecutionHook));
                if (existing is not null)
                    services.Remove(existing);

                services.AddScoped<ICrewExecutionHook>(sp =>
                {
                    var inner = existing is null ? null : (ICrewExecutionHook?)Resolve(sp, existing);
                    observer = new Run.RunEventObserver(events, inner, options.Stream);
                    return observer;
                });
                services.AddSingleton<ILlmUsageSink>(sp =>
                    (ILlmUsageSink)sp.GetRequiredService<ICrewExecutionHook>());
                services.AddSingleton<ILlmDeltaSink>(sp =>
                    (ILlmDeltaSink)sp.GetRequiredService<ICrewExecutionHook>());

                // D6: an observed run never approves on the user's behalf. The runner's
                // AutoApprove fallback is registered by TryAdd, so an explicit singleton
                // here wins without removing anything.
                services.AddSingleton<IHumanInputProvider>(
                    new Run.JsonLinesHumanInputProvider(events, inbound));

                // BUS-05: give the observing process a seat at the hub. A decorator, so local
                // traffic keeps going through the in-memory hub untouched — and only when the
                // host registered a hub at all.
                var hubDescriptor = services.LastOrDefault(d => d.ServiceType == typeof(IEventHub));
                if (hubDescriptor is not null)
                {
                    services.Remove(hubDescriptor);
                    services.AddSingleton<IEventHub>(sp =>
                    {
                        var inner = (IEventHub)Resolve(sp, hubDescriptor)!;
                        bridge = new Run.JsonLinesEventHubBridge(inner, events, options.Client);
                        return bridge;
                    });
                }
            })
            .ConfigureAwait(false);

        if (bridge is not null)
            await bridge.DisposeAsync().ConfigureAwait(false);

        events.Emit(Run.RunEventKinds.RunFinished, new
        {
            success = exitCode == Program.ExitOk,
            exitCode,
            tokens = observer?.TokensUsed ?? 0,
        });

        return exitCode;
    }

    /// <summary>
    /// Materialises a captured service descriptor — the runner registers its hook by
    /// factory, so the descriptor is the only handle on the instance it would have built.
    /// </summary>
    private static object? Resolve(IServiceProvider sp, ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationInstance is { } instance)
            return instance;
        if (descriptor.ImplementationFactory is { } factory)
            return factory(sp);
        return descriptor.ImplementationType is { } type
            ? ActivatorUtilities.CreateInstance(sp, type)
            : null;
    }

    /// <summary>
    /// Maps the shared fields of <see cref="RunCommandOptions"/> onto a <see cref="RunnerOptionsBase"/>
    /// for the shared-runner path (YAML run, <c>--validate</c>, <c>--list-tools</c>). Script-only
    /// fields (<c>--inputs</c>, <c>--inputs-file</c>, <c>--memory-limit-mb</c>) have no YAML equivalent
    /// and are intentionally not carried over.
    /// </summary>
    internal static RunnerOptionsBase ToRunnerOptions(RunCommandOptions options)
        => new YamlRunnerOptions
        {
            ConfigPath = options.ScriptPath,
            SettingsPath = options.SettingsPath,
            Mounts = options.Mounts,
            AllowExternalMounts = options.AllowExternalMounts,
            Verbose = options.Verbose,
            LlmLogEnabled = options.LlmLogEnabled,
            LlmLogPath = options.LlmLogPath,
            Variables = options.Variables,
            InitialContext = options.InitialContext,
            Validate = options.Validate,
            ListTools = options.ListTools,
        };

    private static int ReportEsbuildNotFound(EsbuildNotFoundException ex)
    {
        // Bundling is mandatory (always-bundle mode) so the user must have esbuild
        // installed somewhere we can find it. Surface the diagnostic verbatim — it
        // already enumerates the resolution chain and points at the install command.
        Console.Error.WriteLine($"orkeon run: {ex.Message}");
        Console.Error.WriteLine("  hint: a repo-local copy may live in tools/scripting-esbuild/ — run 'npm install' there.");
        return Program.ExitScriptError;
    }

    private static int ReportEsbuildTranspileError(EsbuildTranspileException ex)
    {
        Console.Error.WriteLine($"orkeon run: esbuild rejected the script:");
        Console.Error.WriteLine(ex.Message);
        return Program.ExitScriptError;
    }

    private static int ReportUnexpectedError(Exception ex)
    {
        // Scripts that throw inside agent .body() callbacks bubble out as
        // PromiseRejectedException → AggregateException → real exception.
        // Print the root cause; keep the outer-type prefix so bug reports
        // still capture the wrapper chain. See JsExceptionUnwrap.
        var root = JsExceptionUnwrap.UnwrapToInnermost(ex);
        var outerTypeHint = ReferenceEquals(root, ex)
            ? ex.GetType().FullName
            : $"{ex.GetType().Name} → {root.GetType().FullName}";
        Console.Error.WriteLine($"orkeon run: unexpected error [{outerTypeHint}]: {root.Message}");
        // Opt-in diagnostics: ORKEON_DEBUG=1 prints the full wrapper chain + stacks
        // (root.Message alone is useless for NullReferenceException-class bugs). The
        // switch is shared with the runner's own diagnostics, hence RunnerEnvironment.
        if (RunnerEnvironment.DebugDiagnostics)
            Console.Error.WriteLine(ex.ToString());
        return Program.ExitRuntimeError;
    }

    private static async Task<int> RunWithHostAsync(RunCommandOptions options, CancellationToken externalCt)
    {
        var fullPath = Path.GetFullPath(options.ScriptPath);
        var scriptDir = Path.GetDirectoryName(fullPath)!;
        var fileName = Path.GetFileName(fullPath);

        // Build the host with the same bootstrap surface as the YAML runner: appsettings,
        // VFS mounts, LLM provider, tools, LLM exchange logging. Plus a /script:ro mount
        // for the script itself.
        var settingsPath = RunnerSettings.ResolveSettingsPath(options.SettingsPath, scriptDir);
        if (settingsPath != null)
            await Console.Error.WriteLineAsync($"Using settings: {settingsPath}").ConfigureAwait(false);

        var cliMounts = options.Mounts.ToList();
        var llmLogPath = options.ResolvedLlmLogPath;

        // The script itself is always implicitly readable — it's the CLI's primary
        // input, not a user-declared mount — so we don't gate it behind
        // --allow-external-mounts. Only LLM log directories outside the cwd require
        // explicit opt-in (parity with the YAML runner's safety stance for writes).
        var cwd = Directory.GetCurrentDirectory();
        var llmLogOutsideCwd = llmLogPath != null && !llmLogPath.StartsWith(cwd, StringComparison.Ordinal);
        if (llmLogOutsideCwd && !options.EffectiveAllowExternalMounts)
        {
            await Console.Error.WriteLineAsync(
                "ERROR: --allow-external-mounts is required when --llm-log-path "
                + "points outside the current working directory "
                + "(or set ORKEON_ALLOW_EXTERNAL_MOUNTS=1).").ConfigureAwait(false);
            await Console.Error.WriteLineAsync($"       llmLogPath  : {llmLogPath}").ConfigureAwait(false);
            await Console.Error.WriteLineAsync($"       cwd         : {cwd}").ConfigureAwait(false);
            return Program.ExitScriptError;
        }

        // 1:1 mount the script directory under /script:ro so ScriptHost.RunAsync can resolve
        // the source through the same IFileSystemService the tools will see. We add it as
        // an "allowed-external" mount regardless of cwd because the script is the input.
        cliMounts.Insert(0, $"{scriptDir}:/script:ro");
        if (llmLogPath != null)
            cliMounts.Insert(1, $"{llmLogPath}:{llmLogPath}:rw");
        // The script directory always needs to be on the security whitelist so the VFS
        // can resolve /script/* even when the user didn't pass --allow-external-mounts.
        var implicitlyAllow = options.EffectiveAllowExternalMounts
            || !scriptDir.StartsWith(cwd, StringComparison.Ordinal);

        var verbosity = Math.Clamp(options.Verbose, 0, 2);

        using var host = RunnerHost.Build(
            settingsPath, cliMounts,
            allowExternalMounts: implicitlyAllow,
            llmLogPath: llmLogPath,
            configureLogging: verbosity > 0
                ? (_, b) => RunnerExecution.ConfigureVerboseLogging(b, verbosity)
                : null,
            configureServices: (ctx, services) =>
            {
                // RAG-03/C3: scripts get the first-class `rag.*` namespace plus the
                // auto-exposed tools.ragSearch / tools.ragIngest. Registration is
                // TryAdd-based and lazy — hosts without an embedding/chat setup only
                // fail if a script actually touches the RAG surface.
                services.AddOrkeonRag(ctx.Configuration);
                // Same registration as `orkeon rag` (RagCommand): the ONNX
                // cross-encoder is what the balanced/quality profiles rerank with.
                // Without it a script asking for either got "Unknown reranker
                // 'onnx'" — two of the five profiles were unreachable from
                // `orkeon run` while being reachable from `orkeon rag`. Weights are
                // embedded (Orkeon.Rag.Onnx.Model) and loaded lazily at first use,
                // so a profile that never reranks pays nothing.
                services.AddOrkeonOnnxReranker();
                services.AddOrkeonRagTools();
            });

        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Orkeon.Scripting.Cli");
        if (llmLogPath != null)
            LogLlmLoggingEnabled(logger, llmLogPath);
        if (verbosity > 0)
            RunnerLogging.LogMounts(cliMounts, logger);

        using var linkCts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        using var lifetime = RunnerExecution.RegisterGracefulShutdown(linkCts, logger);

        // The DI-provided IFileSystemService respects the mount list above; tools resolved
        // from the host will use it natively. JsEngineFactory wraps it for the script body.
        var fileSystem = host.Services.GetRequiredService<IFileSystemService>();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var configuration = host.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
        var tools = host.Services.GetServices<IBaseTool>().ToList();
        // LLM provider is optional — scripts that never call ctx.llm work without one.
        var llmProvider = host.Services.GetService<ILlmProvider>();

        // Scripted ctx.llm.* calls (JsLlmFacade) hit the provider directly, bypassing the
        // throttling ExecutionOrchestrator applies to the YAML/agent path. Wrap the provider
        // in the rate-limited decorator so a dynamic fan-out (one spawned agent per command,
        // fired concurrently) honours the RateLimiting appsettings block instead of opening N
        // simultaneous sockets. Scoped here on purpose — the YAML path keeps its own limiter,
        // so we never double-throttle.
        var llmRateLimiter = host.Services.GetService<ILlmRateLimiter>();
        llmProvider = WrapWithRateLimiter(llmProvider, llmRateLimiter, loggerFactory, logger);

        LogToolsLoaded(logger, tools.Count, llmProvider?.GetType().Name ?? "(none)");

        var cliLimits = ResolveEffectiveLimits(options, configuration, logger);

        // Optional per-tool-call permission gate (F2): opt-in via DI — hosts that register
        // no IPermissionGate keep the ungated ctx.llm.act behaviour.
        var permissionGate = host.Services.GetService<Orkeon.Application.Interfaces.Security.IPermissionGate>();

        // Optional native delta renderer (F5 L3): opt-in via DI — without a sink the act
        // loop keeps its buffered behaviour unless the script passes onDelta.
        var deltaSink = host.Services.GetService<Orkeon.Application.Interfaces.Ports.ILlmDeltaSink>();

        // Optional usage receiver: opt-in via DI — without a sink, per-call LLM usage
        // is simply not observed (no accounting side effects).
        var usageSink = host.Services.GetService<Orkeon.Application.Interfaces.Ports.ILlmUsageSink>();

        // RAG pipelines back the first-class `rag.*` scripting namespace. Resolution is
        // best-effort: a host without embedding/chat defaults must not break scripts
        // that never touch rag.* (the binding itself fails loudly on use when null).
        var ingestionPipeline = SafeGetService<Orkeon.Rag.Abstractions.Interfaces.IIngestionPipeline>(host.Services, logger);
        var ragPipeline = SafeGetService<Orkeon.Rag.Abstractions.Interfaces.IRagPipeline>(host.Services, logger);
        var ragBackend = ingestionPipeline is not null && ragPipeline is not null
            ? new Orkeon.Scripting.Bindings.RagScriptingBackend
            {
                IngestionPipeline = ingestionPipeline,
                RagPipeline = ragPipeline,
                FileSystem = fileSystem,
                // Lets `rag.query({ profile: "corrective" })` select a pipeline per
                // call instead of being stuck with the host-wide
                // Orkeon:Rag:Profile. Best-effort like the two above: absent
                // resolver ⇒ the binding refuses a profile request loudly rather
                // than silently serving the default.
                ProfileResolver = SafeGetService<Orkeon.Rag.Abstractions.Interfaces.IRagProfileResolver>(
                    host.Services, logger),
            }
            : null;

        var engineFactory = new JsEngineFactory(
            limits: cliLimits,
            loggerFactory: loggerFactory,
            configuration: configuration,
            builtInTools: tools,
            llmProvider: llmProvider,
            permissionGate: permissionGate,
            deltaSink: deltaSink,
            ragBackend: ragBackend,
            usageSink: usageSink);

        // ScriptHost stores but does not own/dispose the transpiler, so we keep ownership
        // here and dispose it when this method returns (after RunFromFileAsync completes).
        using var transpiler = ResolveTranspiler();
        var scriptHost = new Orkeon.Scripting.ScriptHost(
            fileSystem,
            transpiler,
            engineFactory,
            loggerFactory.CreateLogger<Orkeon.Scripting.ScriptHost>());

        // Inputs surface (parity with the YAML runner's --var/--initial-context but delivered
        // as a structured `inputs` global to fit the JS DSL). ScriptHost now exposes a
        // pre-execution hook (the `inputsJson` parameter) that parses the JSON into
        // `globalThis.inputs` before evaluation — wire --inputs / --inputs-file through it.
        var inputsJson = options.InputsJson;
        if (string.IsNullOrWhiteSpace(inputsJson) && !string.IsNullOrWhiteSpace(options.InputsFilePath))
        {
            // EXCEPTION-BOOTSTRAP: the inputs file is a user-supplied CLI argument resolved before
            // the VFS is mounted; it is read once, not part of the sandboxed workspace.
            inputsJson = await File.ReadAllTextAsync(options.InputsFilePath, linkCts.Token).ConfigureAwait(false);
        }

        var virtualPath = $"/script/{fileName}";
        // Pass the physical path so esbuild can --bundle relative imports from the
        // entry's directory. ScriptHost still uses virtualPath for VFS reads and logging.
        var result = await scriptHost.RunFromFileAsync(fullPath, virtualPath, linkCts.Token, inputsJson).ConfigureAwait(false);

        Console.WriteLine(SerializeRunResult(result));
        return Program.ExitOk;
    }

    /// <summary>
    /// Resolves an optional service without letting a mis-configured dependency chain
    /// (e.g. a RAG pipeline missing its embedding provider) crash script runs that
    /// never touch the service. Logs the resolution failure at debug level.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort optional resolution: any activation failure must degrade to 'service unavailable' (the scripting binding fails loudly on use), never crash scripts that don't use it.")]
    private static T? SafeGetService<T>(IServiceProvider services, ILogger logger) where T : class
    {
        try
        {
            return services.GetService<T>();
        }
        catch (Exception ex)
        {
            LogOptionalServiceUnavailable(logger, typeof(T).Name, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Wraps <paramref name="llmProvider"/> in the rate-limited decorator when both a provider and
    /// a rate limiter are available; otherwise returns the provider unchanged (possibly null).
    /// </summary>
    private static ILlmProvider? WrapWithRateLimiter(
        ILlmProvider? llmProvider,
        ILlmRateLimiter? llmRateLimiter,
        ILoggerFactory loggerFactory,
        ILogger logger)
    {
        // Scripted ctx.llm.* calls (JsLlmFacade) hit the provider directly, bypassing the
        // throttling ExecutionOrchestrator applies to the YAML/agent path. Wrap the provider
        // in the rate-limited decorator so a dynamic fan-out (one spawned agent per command,
        // fired concurrently) honours the RateLimiting appsettings block instead of opening N
        // simultaneous sockets. Scoped here on purpose — the YAML path keeps its own limiter,
        // so we never double-throttle.
        if (llmProvider is null || llmRateLimiter is null)
            return llmProvider;

        LogScriptedLlmRateLimited(logger);
        return new Orkeon.Infrastructure.LLMs.RateLimitedLlmProvider(
            llmProvider, llmRateLimiter,
            loggerFactory.CreateLogger<Orkeon.Infrastructure.LLMs.RateLimitedLlmProvider>());
    }

    /// <summary>
    /// Resolves the effective Jint sandbox limits for this run: binds the
    /// <c>Orkeon:Scripting:Limits</c> appsettings section over the strict defaults
    /// (the documented opt-in for trusted long runs — e.g. <c>ExecutionTimeout</c>,
    /// which is wall-clock and keeps ticking across awaited tool calls), then applies
    /// the <c>--memory-limit-mb</c> CLI override on top. A value ≤ 0 disables the
    /// memory cap. The CLI flag must MERGE into the bound options, not replace them:
    /// replacing would silently reset <c>ExecutionTimeout</c> back to the 30s default.
    /// </summary>
    internal static ScriptingLimitsOptions ResolveEffectiveLimits(
        RunCommandOptions options,
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        ILogger logger)
    {
        var limits = Microsoft.Extensions.Configuration.ConfigurationBinder
            .Get<ScriptingLimitsOptions>(configuration.GetSection(ScriptingLimitsOptions.SectionName))
            ?? new ScriptingLimitsOptions();

        if (!options.MemoryLimitMb.HasValue)
            return limits;

        var mb = options.MemoryLimitMb.Value;
        var bytes = mb <= 0 ? long.MaxValue : mb * 1024L * 1024L;
        LogMemoryLimitOverridden(
            logger,
            mb <= 0 ? "∞" : mb.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return limits with { MemoryLimitBytes = bytes };
    }

    /// <summary>
    /// Best-effort JSON serialization of the script result. Some scripts persist
    /// <c>globalThis.result = await crew.run()</c> where the result is a CLR record carrying
    /// async-state references that System.Text.Json can't serialize; on failure this falls back
    /// to a ToString() summary so the CLI still emits useful stdout without changing the exit code.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort result serialization: JsonSerializer.Serialize can throw NotSupportedException/InvalidOperationException (or reflection-related errors) on CLR records carrying async-state references, so the failure falls back to a ToString() summary without changing the exit code.")]
    private static string SerializeRunResult(object? result)
    {
        try
        {
            return JsonSerializer.Serialize(new { result }, ResultJsonOptions);
        }
        catch (Exception)
        {
            // JsonSerializer can throw NotSupportedException, InvalidOperationException, or
            // even reflection-related exceptions on CLR objects with async-state fields.
            return JsonSerializer.Serialize(new
            {
                result = result?.ToString() ?? "(null)",
                note = "Result type was not JSON-serializable; emitted ToString() instead.",
            }, FallbackJsonOptions);
        }
    }

    private static EsbuildTranspiler ResolveTranspiler()
    {
        // Always bundle through esbuild — even when the script has no imports — so that:
        //   1. relative 'import { … } from "./helpers.ts"' actually resolves,
        //   2. TS-only syntax (enums, type-only imports, etc.) is consistently stripped
        //      instead of relying on Jint's tolerance for TS-flavoured JS.
        // EsbuildNotFoundException is caught upstream with a clear install hint.
        return new EsbuildTranspiler();
    }

    // --- source-generated logging ---

    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "LLM exchange logging enabled → {LogDir}/llm-exchanges-*.jsonl")]
    static partial void LogLlmLoggingEnabled(ILogger logger, string? logDir);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information,
        Message = "Scripted LLM calls are rate-limited (RateLimiting appsettings honored).")]
    static partial void LogScriptedLlmRateLimited(ILogger logger);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information,
        Message = "Loaded {ToolCount} tool(s); LLM provider: {Llm}")]
    static partial void LogToolsLoaded(ILogger logger, int toolCount, string llm);

    [LoggerMessage(EventId = 4, Level = LogLevel.Information,
        Message = "Jint memory limit overridden to {Mb} MB (--memory-limit-mb)")]
    static partial void LogMemoryLimitOverridden(ILogger logger, string mb);

    [LoggerMessage(EventId = 5, Level = LogLevel.Debug,
        Message = "Optional service {Service} unavailable for scripting bindings: {Reason}")]
    static partial void LogOptionalServiceUnavailable(ILogger logger, string service, string reason);
}
