using System.Collections.Immutable;
using Jint;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Cli.Commands.Scripting.Args;
using Orkeon.Cli.Commands.Scripting.Bindings;
using Orkeon.Cli.Commands.Scripting.Registry;
using Orkeon.Cli.Commands.Scripting.Runtime;
using Orkeon.Domain.FileSystem;
using Orkeon.Scripting;
using Orkeon.Scripting.Toolchain;

namespace Orkeon.Cli.Commands.Scripting.Loading;

/// <summary>
/// Discovers <c>*.cmd.ts</c> scripts under configured virtual directories, evaluates
/// them through Jint, collects <see cref="CommandDescriptor"/> instances, and produces
/// a <see cref="ScriptCommandRegistry"/>.
/// </summary>
/// <remarks>
/// <para>
/// One Jint <see cref="Engine"/> per script, kept warm for the lifetime of the runner
/// (spec §6.2). A per-engine <see cref="SemaphoreSlim"/> serialises invocations
/// defensively (§6.3).
/// </para>
/// <para>
/// All I/O goes through <see cref="IFileSystemService"/>; no direct <c>System.IO</c>
/// usage. The transpiler is injected — production wires
/// <see cref="EsbuildTranspiler"/>, tests can inject <see cref="PassThroughTranspiler"/>.
/// </para>
/// </remarks>
public sealed partial class ScriptCommandLoader
{
    private readonly IFileSystemService _fileSystem;
    private readonly IScriptTranspiler _transpiler;
    private readonly JsEngineFactory _engineFactory;
    private readonly ScriptCommandLoaderOptions _options;
    private readonly ILogger<ScriptCommandLoader> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ScriptServiceLocator _services;
    private readonly Dispatch.CommandDispatchService? _dispatch;
    private readonly Progress.ProgressBroker? _progress;

    public ScriptCommandLoader(
        IFileSystemService fileSystem,
        IScriptTranspiler transpiler,
        JsEngineFactory engineFactory,
        IOptions<ScriptCommandLoaderOptions> options,
        ScriptCommandLoaderDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _transpiler = transpiler ?? throw new ArgumentNullException(nameof(transpiler));
        _engineFactory = engineFactory ?? throw new ArgumentNullException(nameof(engineFactory));
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _loggerFactory = dependencies.LoggerFactory ?? NullLoggerFactory.Instance;
        _logger = _loggerFactory.CreateLogger<ScriptCommandLoader>();
        _services = dependencies.Services ?? ScriptServiceLocator.Empty;
        _dispatch = dependencies.Dispatch;
        _progress = dependencies.Progress;
    }

    /// <summary>
    /// Convenience overload that picks reasonable defaults — used by tests and DI-less
    /// hosts. Production code goes through DI and <see cref="IOptions{TOptions}"/>.
    /// </summary>
    public ScriptCommandLoader(
        IFileSystemService fileSystem,
        IScriptTranspiler transpiler,
        JsEngineFactory engineFactory,
        ScriptCommandLoaderOptions? options = null,
        ILoggerFactory? loggerFactory = null)
        : this(
            fileSystem,
            transpiler,
            engineFactory,
            Microsoft.Extensions.Options.Options.Create(options ?? new ScriptCommandLoaderOptions()),
            new ScriptCommandLoaderDependencies
            {
                LoggerFactory = loggerFactory,
            })
    {
    }

    /// <summary>
    /// Performs the discovery + materialise + validate pipeline and returns a registry plus a load summary.
    /// </summary>
    public async Task<(ScriptCommandRegistry Registry, CommandLoadResult Summary)> LoadAndRegisterAsync(CancellationToken ct)
    {
        if (!_options.Enabled)
        {
            LogCommandsDisabled();
            return (new ScriptCommandRegistry(Array.Empty<ScriptCommand>()),
                    new CommandLoadResult(0, 0, 0, ImmutableArray<string>.Empty));
        }

        var acc = new LoadAccumulator();

        var files = await DiscoverAsync(ct).ConfigureAwait(false);
        if (files.Count == 0)
        {
            LogNoFilesDiscovered();
            return (new ScriptCommandRegistry(Array.Empty<ScriptCommand>()),
                    new CommandLoadResult(0, 0, 0, ImmutableArray<string>.Empty));
        }

        files = CapToMaxScripts(files);

        var materialised = await MaterialiseAllAsync(files, acc, ct).ConfigureAwait(false);
        var validated = ValidateDescriptors(materialised, acc);
        var conflicting = ResolveConflicts(validated, acc);

        var commands = validated
            .Where(t => !conflicting.Contains(t.Descriptor))
            .Select(BuildScriptCommand)
            .ToList();

        var builtins = _dispatch is not null
            ? Dispatch.Commands.DispatchBuiltinCommands.Create(_dispatch)
            : null;
        var registry = new ScriptCommandRegistry(commands, builtins);
        var summary = new CommandLoadResult(commands.Count, acc.Skipped, acc.Conflicts, acc.Errors.ToImmutable());
        LogCommandsReady(summary.Loaded, summary.SkippedScripts, summary.Conflicts);
        return (registry, summary);
    }

    /// <summary>Mutable running tally of load errors / skips / conflicts shared by the load helpers.</summary>
    private sealed class LoadAccumulator
    {
        public ImmutableArray<string>.Builder Errors { get; } = ImmutableArray.CreateBuilder<string>();
        public int Skipped { get; set; }
        public int Conflicts { get; set; }
    }

    private List<string> CapToMaxScripts(List<string> files)
    {
        if (files.Count <= _options.MaxScripts)
            return files;

        var dropped = files.Count - _options.MaxScripts;
        LogCapApplied(files.Count, _options.MaxScripts, dropped);
        return files.Take(_options.MaxScripts).ToList();
    }

    private async Task<List<(Engine Engine, SemaphoreSlim Lock, CompletionDrainQueue Drain, IReadOnlyList<CommandDescriptor> Descriptors)>>
        MaterialiseAllAsync(List<string> files, LoadAccumulator acc, CancellationToken ct)
    {
        var materialised = new List<(Engine Engine, SemaphoreSlim Lock, CompletionDrainQueue Drain, IReadOnlyList<CommandDescriptor> Descriptors)>();
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var entry = await MaterialiseAsync(file, ct).ConfigureAwait(false);
                materialised.Add(entry);
            }
            catch (Exception ex)
            {
                acc.Skipped++;
                var msg = $"Failed to load '{file}': {ex.Message}";
                acc.Errors.Add(msg);
                LogLoadFailed(ex, file);
                if (_options.FailFastOnInvalidScript)
                    throw;
            }
        }
        return materialised;
    }

    // Per-descriptor validation (kept inside the materialise loop would conflate
    // load-time and validation-time errors; separating clarifies the load summary).
    private List<(CommandDescriptor Descriptor, Engine Engine, SemaphoreSlim Lock, CompletionDrainQueue Drain)>
        ValidateDescriptors(
            List<(Engine Engine, SemaphoreSlim Lock, CompletionDrainQueue Drain, IReadOnlyList<CommandDescriptor> Descriptors)> materialised,
            LoadAccumulator acc)
    {
        var validated = new List<(CommandDescriptor Descriptor, Engine Engine, SemaphoreSlim Lock, CompletionDrainQueue Drain)>();
        foreach (var (engine, sem, drain, descriptors) in materialised)
        {
            foreach (var d in descriptors)
            {
                if (TryResolveDescriptor(d, acc, out var resolved))
                    validated.Add((resolved, engine, sem, drain));
            }
        }
        return validated;
    }

    private bool TryResolveDescriptor(CommandDescriptor d, LoadAccumulator acc, out CommandDescriptor resolved)
    {
        resolved = d;

        var verdict = CommandDescriptorValidator.Validate(d);
        if (!verdict.IsValid)
        {
            acc.Skipped++;
            var msg = $"Invalid descriptor in '{d.SourceVirtualPath}': {verdict.Message}";
            acc.Errors.Add(msg);
            LogReasonError(msg);
            if (_options.FailFastOnInvalidScript)
                throw new InvalidOperationException(msg);
            return false;
        }

        // Deliberate default-shadowing (e.g. a surface redefining /clear) is allowed but
        // never silent — the scripted version will win name resolution in the runner.
        // CA1873: the string.Join stays out of the disabled-logging path.
        if (_logger.IsEnabled(LogLevel.Information))
        {
            var shadowed = CommandDescriptorValidator.GetShadowedDefaults(d);
            if (shadowed.Count > 0)
            {
                var shadowedNames = string.Join(", ", shadowed);
                LogDefaultShadowed(d.Name, d.SourceVirtualPath, shadowedNames);
            }
        }

        // Phase 3: resolve the typed args schema from the captured JS value.
        try
        {
            var parsed = ArgsSchemaParser.Parse(d.ArgsSchemaJson);
            resolved = d with { ArgsSchema = parsed };
            return true;
        }
        catch (ArgsSchemaException schemaEx)
        {
            acc.Skipped++;
            var msg = $"Invalid args schema in '{d.SourceVirtualPath}': {schemaEx.Message}";
            acc.Errors.Add(msg);
            LogReasonError(msg);
            if (_options.FailFastOnInvalidScript)
                throw new InvalidOperationException(msg, schemaEx);
            return false;
        }
    }

    private HashSet<CommandDescriptor> ResolveConflicts(
        List<(CommandDescriptor Descriptor, Engine Engine, SemaphoreSlim Lock, CompletionDrainQueue Drain)> validated,
        LoadAccumulator acc)
    {
        var conflictList = CommandDescriptorValidator.ValidateGlobalUniqueness(validated.Select(t => t.Descriptor).ToList());
        var conflicting = new HashSet<CommandDescriptor>(conflictList.Select(c => c.Conflicting));
        foreach (var (descriptor, reason) in conflictList)
        {
            acc.Conflicts++;
            var msg = $"Conflict: '{descriptor.SourceVirtualPath}' — {reason}";
            acc.Errors.Add(msg);
            if (_options.ContinueOnConflict)
            {
                LogReasonWarning(msg);
            }
            else
            {
                LogReasonError(msg);
                throw new InvalidOperationException(msg);
            }
        }
        return conflicting;
    }

    private ScriptCommand BuildScriptCommand(
        (CommandDescriptor Descriptor, Engine Engine, SemaphoreSlim Lock, CompletionDrainQueue Drain) t)
        => new(
            new ScriptCommandBinding
            {
                Engine = t.Engine,
                Descriptor = t.Descriptor,
                EngineLock = t.Lock,
                DrainQueue = t.Drain,
            },
            new ScriptCommandServices
            {
                Services = _services,
                Dispatch = _dispatch,
                Progress = _progress,
                Logger = _loggerFactory.CreateLogger($"ScriptCommand:{t.Descriptor.Name}"),
            });

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Per-directory fault barrier: a failure enumerating one script directory is logged and skipped so the remaining directories are still discovered.")]
    private async Task<List<string>> DiscoverAsync(CancellationToken ct)
    {
        var collected = new List<string>();
        foreach (var dir in _options.Directories)
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            try
            {
                var enumOptions = new VirtualEnumerationOptions(
                    Recursive: true,
                    FollowSymlinks: false,
                    SearchPattern: _options.SearchPattern);

                await foreach (var entry in _fileSystem.EnumerateFilesAsync(dir, enumOptions, ct).ConfigureAwait(false))
                {
                    if (entry.Kind == VirtualEntryKind.File)
                        collected.Add(entry.VirtualPath);
                }
            }
            catch (Exception ex)
            {
                LogEnumerateFailed(ex, dir);
            }
        }

        // Stable ordinal sort — drives discovery order (and first-wins on conflict).
        collected.Sort(StringComparer.Ordinal);
        return collected;
    }

    private async Task<(Engine, SemaphoreSlim, CompletionDrainQueue, IReadOnlyList<CommandDescriptor>)> MaterialiseAsync(
        string virtualPath,
        CancellationToken ct)
    {
        var source = await _fileSystem.TryReadAllTextAsync(virtualPath, ct).ConfigureAwait(false)
            ?? throw new FileNotFoundException($"Script not found: '{virtualPath}'", virtualPath);

        // Resolve to a physical path so esbuild can bundle relative imports. The VFS
        // returns a denial result if the mount lacks Read rights; in that case
        // BundleFromFileAsync is skipped and we fall back to the in-memory transpile path
        // (imports won't resolve, but raw syntax stripping still works).
        var validation = _fileSystem.ResolveAndValidate(virtualPath, FileAccessRights.Read);
        var physicalPath = validation.IsAllowed ? validation.ResolvedPath : null;

        string js;
        if (_options.EsbuildTranspile)
        {
            js = physicalPath is not null
                ? await _transpiler.BundleFromFileAsync(physicalPath, source, ct).ConfigureAwait(false)
                : await _transpiler.TranspileAsync(source, ct).ConfigureAwait(false);
        }
        else
        {
            // Test/debug only — assume source is already valid JS (no TS syntax).
            js = source;
        }

        var engine = _engineFactory.Create();
        var collector = new CommandDescriptorCollector(virtualPath);
        DefineCommandBinding.Register(engine, collector);
        DefineAsyncCommandBinding.Register(engine, collector);

        // Neutral console shim at load time — Phase 2 requirement (spec §13). Re-bound
        // per-invocation by ContextBinding.Push to forward into ctx.log.
        ConsoleShimBinding.ApplyForLoad(engine, _loggerFactory.CreateLogger($"ScriptLoad:{virtualPath}"));

        // Mirror ScriptHost's top-level-await fallback so scripts using
        // `await x()` at module scope work as expected.
        try
        {
            await engine.EvaluateAsync(js, virtualPath, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (
            (ex is Jint.Runtime.JavaScriptException || ex is Acornima.ParseErrorException)
            && (ex.Message.Contains("await is only valid", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("top level bodies of modules", StringComparison.OrdinalIgnoreCase)))
        {
            var wrapped = "(async function __orkeonCmdMain(){\n" + js + "\n})();";
            var completion = await engine.EvaluateAsync(wrapped, virtualPath, ct).ConfigureAwait(false);
            if (completion.IsPromise())
                await Jint.JsValueExtensions.UnwrapIfPromiseAsync(completion, ct).ConfigureAwait(false);
        }

        collector.Freeze();

        return (engine, new SemaphoreSlim(1, 1), new CompletionDrainQueue(), collector.Descriptors);
    }

    // --- source-generated logging ---

    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "Scripted commands disabled by options; returning empty registry.")]
    partial void LogCommandsDisabled();

    [LoggerMessage(EventId = 2, Level = LogLevel.Information,
        Message = "No *.cmd.ts files discovered under configured directories.")]
    partial void LogNoFilesDiscovered();

    [LoggerMessage(EventId = 3, Level = LogLevel.Information,
        Message = "Scripted commands ready: {Loaded} loaded, {Skipped} skipped, {Conflicts} conflicts.")]
    partial void LogCommandsReady(int loaded, int skipped, int conflicts);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning,
        Message = "Discovered {Total} *.cmd.ts files; loading only the first {Cap} (MaxScripts), {Dropped} ignored.")]
    partial void LogCapApplied(int total, int cap, int dropped);

    [LoggerMessage(EventId = 5, Level = LogLevel.Error,
        Message = "Failed to load scripted command file {VirtualPath}")]
    partial void LogLoadFailed(Exception ex, string virtualPath);

    [LoggerMessage(EventId = 6, Level = LogLevel.Error, Message = "{Reason}")]
    partial void LogReasonError(string reason);

    [LoggerMessage(EventId = 7, Level = LogLevel.Warning, Message = "{Reason}")]
    partial void LogReasonWarning(string reason);

    [LoggerMessage(EventId = 8, Level = LogLevel.Warning,
        Message = "Failed to enumerate {Directory}; skipping.")]
    partial void LogEnumerateFailed(Exception ex, string directory);

    [LoggerMessage(EventId = 9, Level = LogLevel.Information,
        Message = "Scripted command '{Name}' ({VirtualPath}) shadows the built-in default(s): {Shadowed}. The scripted version wins name resolution.")]
    partial void LogDefaultShadowed(string name, string virtualPath, string shadowed);
}

/// <summary>
/// Supporting collaborators for <see cref="ScriptCommandLoader"/>: the optional logger factory,
/// service locator, and dispatch service. Grouped to keep the loader constructor narrow.
/// Descriptor validation and prompt handling are provided by the static
/// <see cref="CommandDescriptorValidator"/> and <see cref="PromptDispatcher"/> respectively,
/// so they are no longer carried here.
/// </summary>
public sealed record ScriptCommandLoaderDependencies
{
    public ILoggerFactory? LoggerFactory { get; init; }
    public ScriptServiceLocator? Services { get; init; }
    public Dispatch.CommandDispatchService? Dispatch { get; init; }
    public Progress.ProgressBroker? Progress { get; init; }
}
