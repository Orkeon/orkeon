using Jint;
using Jint.Native;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.FileSystem;
using Orkeon.Scripting.Toolchain;
using Orkeon.Scripting.Versioning;

namespace Orkeon.Scripting;

/// <summary>
/// Loads a <c>.ork.ts</c> script from the virtual file system and runs it through Jint.
/// Returns the value of the last expression (or of <c>globalThis.result</c> / a script-set
/// variable named <c>result</c> when present).
/// </summary>
/// <remarks>
/// Minimal SCR-01 surface: no TypeScript stripping (SCR-02), no builders (SCR-03..SCR-06),
/// no sandboxing beyond <see cref="JsEngineFactory"/> defaults.
/// </remarks>
public sealed partial class ScriptHost
{
    private readonly IFileSystemService _fileSystem;
    private readonly IScriptTranspiler _transpiler;
    private readonly JsEngineFactory _engineFactory;
    private readonly ILogger<ScriptHost> _logger;

    /// <summary>
    /// Creates a host that loads scripts via <paramref name="fileSystem"/>, transpiles
    /// them through <paramref name="transpiler"/>, and runs them in engines built by
    /// <paramref name="engineFactory"/>.
    /// </summary>
    public ScriptHost(
        IFileSystemService fileSystem,
        IScriptTranspiler transpiler,
        JsEngineFactory engineFactory,
        ILogger<ScriptHost>? logger = null)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _transpiler = transpiler ?? throw new ArgumentNullException(nameof(transpiler));
        _engineFactory = engineFactory ?? throw new ArgumentNullException(nameof(engineFactory));
        _logger = logger ?? NullLogger<ScriptHost>.Instance;
    }

    /// <summary>
    /// Convenience overload using the no-op <see cref="PassThroughTranspiler"/> — handy in
    /// tests that load pre-transpiled JS fixtures without depending on the esbuild binary.
    /// </summary>
    public ScriptHost(
        IFileSystemService fileSystem,
        JsEngineFactory engineFactory,
        ILogger<ScriptHost>? logger = null)
        : this(fileSystem, PassThroughTranspiler.Instance, engineFactory, logger)
    {
    }

    /// <summary>
    /// Loads the script at <paramref name="virtualPath"/> and executes it.
    /// </summary>
    /// <param name="virtualPath">Virtual path resolved through <see cref="IFileSystemService"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The script's resulting value: the last expression evaluated, or the value of a
    /// top-level variable named <c>result</c> if the last expression is undefined.
    /// </returns>
    /// <exception cref="FileNotFoundException">If the script does not exist at the given virtual path.</exception>
    public Task<object?> RunAsync(string virtualPath, CancellationToken ct)
        => RunFromFileAsync(physicalPath: null, virtualPath, ct);

    /// <summary>
    /// Same as <see cref="RunAsync(string, CancellationToken)"/> but additionally hands the
    /// script's physical path to the transpiler so it can <c>--bundle</c> relative imports.
    /// The virtual path is still used for VFS-backed version validation and logging.
    /// </summary>
    /// <param name="physicalPath">
    /// On-disk path of the entry script (passed to esbuild for import resolution). When
    /// <c>null</c>, falls back to the in-memory transpile path — imports will not resolve.
    /// </param>
    /// <param name="virtualPath">Virtual path resolved through <see cref="IFileSystemService"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="inputsJson">Optional JSON injected as a <c>globalThis.inputs</c> object before evaluation.</param>
    public async Task<object?> RunFromFileAsync(
        string? physicalPath, string virtualPath, CancellationToken ct, string? inputsJson = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualPath);

        var (engine, completion) = await EvaluateAsync(physicalPath, virtualPath, ct, inputsJson).ConfigureAwait(false);

        // Convention: scripts that build a crew but can't (or shouldn't) `await crew.run()`
        // at the top level — because of Jint's re-entrant promise-unwrap dead-lock — can
        // hand off via `globalThis.crew = crewBuilder()…build()`. ScriptHost then invokes
        // `RunAsync` directly on the CLR object, side-stepping the JS event loop entirely.
        var crewGlobal = engine.GetValue("crew");
        if (!crewGlobal.IsUndefined() && !crewGlobal.IsNull())
        {
            var clr = crewGlobal.ToObject();
            if (clr is Orkeon.Scripting.Runtime.JsCrew jsCrew)
            {
                LogCrewExported(jsCrew.name);
                var crewResult = await jsCrew.RunAsync(null, ct).ConfigureAwait(false);
                return new
                {
                    crewName = jsCrew.name,
                    finalOutput = crewResult.output ?? "(no aggregated output)",
                    tasks = crewResult.tasks.Select(t => new
                    {
                        name = t.name,
                        durationMs = t.durationMs,
                    }).ToArray(),
                };
            }
        }

        if (completion.IsUndefined())
        {
            // Fallback: scripts that assign their output to a top-level `result` variable.
            var resultVar = engine.GetValue("result");
            if (!resultVar.IsUndefined())
                return resultVar.ToObject();
            return null;
        }

        return completion.ToObject();
    }

    /// <summary>
    /// Evaluates the script at <paramref name="virtualPath"/> and returns the
    /// <see cref="Orkeon.Scripting.Runtime.JsCrew"/> handed off via
    /// <c>globalThis.crew = crewBuilder()…build()</c>. Unlike
    /// <see cref="RunFromFileAsync(string?, string, CancellationToken, string?)"/>, this does NOT
    /// invoke <c>JsCrew.RunAsync</c>: the returned object is meant to be adapted to a
    /// <c>CrewConfiguration</c> and run through the full
    /// <c>ICrewOrchestrationService</c> pipeline (deliverable resolvers, telemetry,
    /// AutoSummaryWriter), giving <c>.ork.ts</c> crew definitions feature parity with the
    /// YAML path.
    /// </summary>
    /// <param name="physicalPath">
    /// On-disk path of the entry script (passed to esbuild for import resolution). When
    /// <c>null</c>, falls back to the in-memory transpile path — imports will not resolve.
    /// </param>
    /// <param name="virtualPath">Virtual path resolved through <see cref="IFileSystemService"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="inputsJson">Optional JSON injected as a <c>globalThis.inputs</c> object before evaluation.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the script does not export a <c>globalThis.crew</c> handle, or exports a
    /// value that is not a <see cref="Orkeon.Scripting.Runtime.JsCrew"/> instance.
    /// </exception>
    public async Task<Orkeon.Scripting.Runtime.JsCrew> LoadCrewFromFileAsync(
        string? physicalPath, string virtualPath, CancellationToken ct, string? inputsJson = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualPath);

        var (engine, _) = await EvaluateAsync(physicalPath, virtualPath, ct, inputsJson).ConfigureAwait(false);

        var crewGlobal = engine.GetValue("crew");
        if (crewGlobal.IsUndefined() || crewGlobal.IsNull())
            throw new InvalidOperationException(
                $"Script '{virtualPath}' did not assign globalThis.crew. " +
                $"The runner expects: `(globalThis as any).crew = crewBuilder()…build();`.");

        var clr = crewGlobal.ToObject();
        if (clr is not Orkeon.Scripting.Runtime.JsCrew jsCrew)
            throw new InvalidOperationException(
                $"Script '{virtualPath}' assigned globalThis.crew to a {clr?.GetType().FullName ?? "null"} " +
                $"instead of an Orkeon.Scripting.Runtime.JsCrew. " +
                $"Use `crewBuilder()…build()` to build the crew.");

        LogCrewLoaded(jsCrew.name, virtualPath);
        return jsCrew;
    }

    /// <summary>
    /// Shared evaluation prologue used by both <see cref="RunFromFileAsync"/> and
    /// <see cref="LoadCrewFromFileAsync"/>: reads the source, transpiles it, runs it
    /// through Jint (with the top-level-await fallback), and unwraps the resulting
    /// promise when needed. Returns the engine plus the final completion value.
    /// </summary>
    private async Task<(Jint.Engine engine, JsValue completion)> EvaluateAsync(
        string? physicalPath, string virtualPath, CancellationToken ct, string? inputsJson = null)
    {
        var source = await _fileSystem.TryReadAllTextAsync(virtualPath, ct).ConfigureAwait(false);
        if (source is null)
            throw new FileNotFoundException($"Script not found: '{virtualPath}'", virtualPath);

        ct.ThrowIfCancellationRequested();

        var version = VersionDirectiveParser.ParseAndValidate(source);
        LogVersionDeclared(virtualPath, version);

        // Bundle when the host knows the physical path (CLI entry point) so esbuild can
        // resolve relative imports from disk. Otherwise fall back to the in-memory
        // transpile path — used by tests that build sources programmatically without
        // touching the disk.
        var js = physicalPath is not null
            ? await _transpiler.BundleFromFileAsync(physicalPath, source, ct).ConfigureAwait(false)
            : await _transpiler.TranspileAsync(source, ct).ConfigureAwait(false);

        LogExecutingScript(virtualPath, source.Length, js.Length);

        var engine = _engineFactory.Create();

        // Pre-execution globals hook: expose a structured `inputs` global before the script
        // runs (SPEC §6 option (a); generalises the pending --inputs work in RunCommand.cs). The
        // caller hands us a JSON string; we parse it inside the engine so `inputs.foo` is a
        // native JS object with proper property access, not a CLR interop wrapper.
        if (!string.IsNullOrWhiteSpace(inputsJson))
        {
            engine.SetValue("__orkeonInputsJson", inputsJson);
            engine.Evaluate("globalThis.inputs = JSON.parse(__orkeonInputsJson);");
            LogInputsInjected(virtualPath, inputsJson!.Length);
        }

        // Jint runs source in script mode, which forbids top-level `await`. We try the
        // raw source first (keeps `var result = 42` style scripts working as documented)
        // and fall back to an async-IIFE wrap when the parser rejects top-level await.
        JsValue completion;
        try
        {
            completion = engine.Evaluate(js);
        }
        catch (Exception ex) when (
            (ex is Jint.Runtime.JavaScriptException || ex is Acornima.ParseErrorException)
            && (ex.Message.Contains("await is only valid", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("top level bodies of modules", StringComparison.OrdinalIgnoreCase)))
        {
            LogTopLevelAwaitWrap(virtualPath);
            var wrapped = "(async function __orkeonMain(){\n" + js + "\n})();";
            completion = engine.Evaluate(wrapped);
        }

        if (completion.IsPromise())
        {
            try
            {
                // Jint.JsValueExtensions.UnwrapIfPromiseAsync bakes in a hard 10s
                // ceiling that cannot be lifted from outside the engine — long
                // top-level scripts (async pre-computation with many tool calls)
                // get rejected with "Timeout of 00:00:10 reached". We use the
                // synchronous UnwrapIfPromise(TimeSpan) overload via Task.Run
                // so the caller's await semantics are preserved while the
                // host-time budget actually matches LoadTimeout.
                completion = await Task.Run(
                    () => Jint.JsValueExtensions.UnwrapIfPromise(completion, LoadPromiseTimeout),
                    ct).ConfigureAwait(false);
            }
            catch (Jint.Runtime.PromiseRejectedException ex)
            {
                throw new InvalidOperationException(
                    $"Script rejected with: {ex.RejectedValue}", ex);
            }
        }

        return (engine, completion);
    }

    /// <summary>
    /// Maximum wall-time the host waits for a top-level async script body
    /// (the IIFE wrap that holds top-level <c>await</c>s) to settle. Mirrors
    /// the equivalent constant in <see cref="Runtime.JsCrew"/> so a long
    /// pre-computation phase never trips the default Jint 10s limit.
    /// </summary>
    private static readonly TimeSpan LoadPromiseTimeout = TimeSpan.FromMinutes(30);

    // --- source-generated logging ---

    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "Script exported globalThis.crew={CrewName}; invoking RunAsync from host.")]
    private partial void LogCrewExported(string crewName);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information,
        Message = "Loaded crew '{CrewName}' from {VirtualPath} (script handed off via globalThis.crew).")]
    private partial void LogCrewLoaded(string crewName, string virtualPath);

    [LoggerMessage(EventId = 3, Level = LogLevel.Debug,
        Message = "Script {VirtualPath} declares orkeon-script version {Version}")]
    private partial void LogVersionDeclared(string virtualPath, string version);

    [LoggerMessage(EventId = 4, Level = LogLevel.Debug,
        Message = "Executing script {VirtualPath} ({SourceBytes} → {JsBytes} bytes)")]
    private partial void LogExecutingScript(string virtualPath, int sourceBytes, int jsBytes);

    [LoggerMessage(EventId = 5, Level = LogLevel.Debug,
        Message = "Injected `inputs` global into {VirtualPath} ({Bytes} bytes).")]
    private partial void LogInputsInjected(string virtualPath, int bytes);

    [LoggerMessage(EventId = 6, Level = LogLevel.Debug,
        Message = "Script {VirtualPath} uses top-level await; wrapping in async IIFE.")]
    private partial void LogTopLevelAwaitWrap(string virtualPath);
}
