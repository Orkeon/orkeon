using System.Text.Json;
using Jint;
using Jint.Native;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Cli.Commands.Scripting.Dispatch;
using Orkeon.Domain.FileSystem;
using Orkeon.Scripting;

namespace Orkeon.Cli.Commands.Scripting.Runtime;

#pragma warning disable IDE1006 // camelCase methods: this CLR type is exposed to scripts as ctx.services.get("script-host")
/// <summary>
/// Host-side facade exposed to scripts as <c>script-host</c> (exp 07 SPEC §6). Lets a
/// <c>*.cmd.ts</c> launch a <c>crew.ork.ts</c> by name and pass it an input, bridging the
/// command-runtime (<c>Orkeon.Cli.Commands.Scripting</c>) to the crew-runtime
/// (<c>Orkeon.Scripting.ScriptHost</c>, which honours <c>.body()</c> + <c>ctx.llm</c>).
/// </summary>
/// <remarks>
/// <para>
/// This is the "host engine" the control plane calls for long work (SPEC §2): the command
/// only <em>talks</em> to it. <see cref="runCrew"/> runs synchronously (short crews, bounded
/// by <see cref="ScriptHostFacadeOptions.RunCrewTimeout"/>);
/// <see cref="runCrewAsync"/> posts the run on a pool thread and returns a ticket, reusing the
/// exact ticket → <c>completed</c> drain cycle as <c>commands.post</c> via
/// <see cref="CommandDispatchService.postWork"/>.
/// </para>
/// <para>
/// Crews are resolved as <c>&lt;dir&gt;/&lt;name&gt;/crew.ork.ts</c> across
/// <see cref="ScriptHostFacadeOptions.CrewDirectories"/>. Input is handed to the crew engine
/// as a <c>globalThis.inputs</c> JS object (parsed from JSON inside the engine — SPEC §6.2 (a)).
/// </para>
/// </remarks>
public sealed partial class ScriptHostFacade
{
    private readonly ScriptHost _host;
    private readonly IFileSystemService _fileSystem;
    private readonly ScriptHostFacadeOptions _options;
    private readonly CommandDispatchService? _dispatch;
    private readonly ILogger _logger;

    public ScriptHostFacade(
        ScriptHost host,
        IFileSystemService fileSystem,
        IOptions<ScriptHostFacadeOptions> options,
        CommandDispatchService? dispatch = null,
        ILogger<ScriptHostFacade>? logger = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _dispatch = dispatch;
        _logger = logger ?? (ILogger)NullLogger.Instance;
    }

    // ---- JS surface -----------------------------------------------------------------------

    /// <summary>
    /// Synchronously runs <c>crews/&lt;name&gt;/crew.ork.ts</c>, waits for completion, and returns
    /// its output. JS <c>await</c> on the returned value resolves to it (we return the value
    /// directly rather than a Task, matching <c>commands.request</c>). For short crews only:
    /// the wait is bounded by <see cref="ScriptHostFacadeOptions.RunCrewTimeout"/> (default
    /// 10 minutes) so a crew that never finishes cannot freeze the REPL — on expiry the script
    /// receives a <see cref="TimeoutException"/> and the abandoned run is cancelled
    /// cooperatively. Long workflows should use <see cref="runCrewAsync"/> instead.
    /// </summary>
    /// <exception cref="TimeoutException">
    /// The crew did not complete within <see cref="ScriptHostFacadeOptions.RunCrewTimeout"/>.
    /// </exception>
    // Assumed-blocking by design — audited (ANT-007/ANT-010, round-02); see docs/architecture/scripting.md.
    public CrewRunOutput runCrew(string name, JsValue? input = null)
    {
        var configured = _options.RunCrewTimeout;
        var timeout = configured > TimeSpan.Zero ? configured : Timeout.InfiniteTimeSpan;
        var inputsJson = ToInputsJson(input);
        using var cts = new CancellationTokenSource();

        // Task.Run guarantees the (possibly synchronously-completing) Jint evaluation happens
        // off this thread, so the bounded wait below can always observe the timeout.
        var run = Task.Run(() => RunCrewCoreAsync(name, inputsJson, cts.Token), CancellationToken.None);
        try
        {
            return run.WaitAsync(timeout).GetAwaiter().GetResult();
        }
        catch (TimeoutException) when (!run.IsCompleted)
        {
            cts.Cancel(); // best effort: aborts the run at its next cooperative checkpoint.
            ObserveAbandonedRun(run);
            throw new TimeoutException(
                $"script-host.runCrew('{name}') did not complete within {configured} " +
                $"({nameof(ScriptHostFacadeOptions)}.{nameof(ScriptHostFacadeOptions.RunCrewTimeout)}, " +
                $"section '{ScriptHostFacadeOptions.SectionName}'). runCrew is meant for short crews; " +
                "use runCrewAsync (ticket + completed callback) for long workflows, or raise the timeout.");
        }
    }

    /// <summary>
    /// Posts <c>crews/&lt;name&gt;/crew.ork.ts</c> on a pool thread and returns a ticket immediately.
    /// The completion is drained onto the engine thread → a <c>defineAsyncCommand</c>'s
    /// <c>completed(result)</c>. The <c>result.payload</c> is the crew's summary. For long workflows.
    /// </summary>
    public string runCrewAsync(string name, JsValue? input = null)
    {
        if (_dispatch is null)
            throw new InvalidOperationException(
                "script-host.runCrewAsync requires the command-dispatch substrate (AddScriptCommands).");

        var safeName = SanitizeName(name);
        var inputsJson = ToInputsJson(input);
        return _dispatch.postWork(safeName, $"crew:{safeName}", async ct =>
        {
            var output = await RunCrewCoreAsync(safeName, inputsJson, ct).ConfigureAwait(false);
            return new CommandResponse(
                agent: "script-host",
                intent: safeName,
                success: output.ok,
                payload: output.summary ?? string.Empty,
                error: output.error);
        });
    }

    /// <summary>Lists the discovered crew names (directories holding a crew entry file).</summary>
    // Assumed-blocking by design — audited (ANT-007/ANT-010, round-02); see docs/architecture/scripting.md.
    public string[] listCrews()
        => ListCrewsCoreAsync(CancellationToken.None).GetAwaiter().GetResult();

    // ---- internals ------------------------------------------------------------------------

    /// <summary>
    /// Observes a timed-out run so a late fault never surfaces as an unobserved task exception.
    /// (Pure-JS busy loops that ignore the cooperative cancellation are eventually reaped by the
    /// Jint sandbox's own <c>TimeoutInterval</c> — see <c>ScriptingLimitsOptions.ExecutionTimeout</c>.)
    /// </summary>
    private static void ObserveAbandonedRun(Task<CrewRunOutput> run)
        => _ = run.ContinueWith(
            static t => _ = t.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Crew-run fault barrier: any failure validating or running a crew is logged and converted to a CrewRunOutput.Failure so it cannot crash the script host; cancellation is rethrown.")]
    private async Task<CrewRunOutput> RunCrewCoreAsync(string name, string? inputsJson, CancellationToken ct)
    {
        string safeName;
        try { safeName = SanitizeName(name); }
        catch (Exception ex) { return CrewRunOutput.Failure(ex.Message); }

        var virtualPath = await ResolveCrewPathAsync(safeName, ct).ConfigureAwait(false);
        if (virtualPath is null)
        {
            var dirs = _options.CrewDirectories.IsDefaultOrEmpty ? "(none configured)" : string.Join(", ", _options.CrewDirectories);
            return CrewRunOutput.Failure($"crew '{safeName}' not found under: {dirs}");
        }

        // Resolve a physical path so esbuild can bundle relative imports (application/prompts.ts)  —
        // fall back to the in-memory transpile path when the mount denies Read.
        var validation = _fileSystem.ResolveAndValidate(virtualPath, FileAccessRights.Read);
        var physicalPath = validation.IsAllowed ? validation.ResolvedPath : null;

        try
        {
            var result = await _host.RunFromFileAsync(physicalPath, virtualPath, ct, inputsJson).ConfigureAwait(false);
            return MapResult(result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Full exception (stack included) goes to the logs; the transcript-facing error
            // is the concise root cause — a crew failure used to dump the whole stringified
            // PromiseRejectedException into the REPL.
            LogCrewRunFailed(ex, safeName);
            return CrewRunOutput.Failure(ConciseErrors.Message(ex));
        }
    }

    private async Task<string?> ResolveCrewPathAsync(string safeName, CancellationToken ct)
    {
        foreach (var dir in _options.CrewDirectories)
        {
            var candidate = CombineVirtual(dir, safeName, _options.CrewFileName);
            if (await _fileSystem.ExistsAsync(candidate, ct).ConfigureAwait(false))
                return candidate;
        }
        return null;
    }

    private async Task<string[]> ListCrewsCoreAsync(CancellationToken ct)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        var suffix = "/" + _options.CrewFileName;
        foreach (var dir in _options.CrewDirectories)
        {
            await CollectCrewNamesFromDirectoryAsync(dir, suffix, names, ct).ConfigureAwait(false);
        }
        return names.ToArray();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Per-directory fault barrier: a failure enumerating one crew directory is logged and skipped so the remaining directories are still listed.")]
    private async Task CollectCrewNamesFromDirectoryAsync(string dir, string suffix, SortedSet<string> names, CancellationToken ct)
    {
        IAsyncEnumerable<VirtualFileEntry> entries;
        try
        {
            entries = _fileSystem.EnumerateFilesAsync(dir, new VirtualEnumerationOptions(Recursive: true), ct);
        }
        catch (Exception ex)
        {
            LogEnumerateCrewDirFailed(ex, dir);
            return;
        }

        await foreach (var entry in entries.WithCancellation(ct).ConfigureAwait(false))
        {
            var crewName = TryExtractCrewName(entry, suffix);
            if (crewName is not null) names.Add(crewName);
        }
    }

    /// <summary>
    /// Returns the crew name (immediate parent directory of the entry) for a crew entry file,
    /// or <c>null</c> when the entry is not a matching crew file.
    /// </summary>
    private static string? TryExtractCrewName(VirtualFileEntry entry, string suffix)
    {
        if (entry.Kind != VirtualEntryKind.File) return null;
        if (!entry.VirtualPath.EndsWith(suffix, StringComparison.Ordinal)) return null;

        // crew name = immediate parent directory name of the entry.
        var withoutFile = entry.VirtualPath[..^suffix.Length];
        var slash = withoutFile.LastIndexOf('/');
        var crewName = slash >= 0 ? withoutFile[(slash + 1)..] : withoutFile;
        return crewName.Length > 0 ? crewName : null;
    }

    private static CrewRunOutput MapResult(object? result)
    {
        if (result is null)
            return new CrewRunOutput { ok = true, summary = null };
        if (result is string s)
            return new CrewRunOutput { ok = true, summary = s, artifacts = s };

        // ScriptHost returns an anonymous { crewName, finalOutput, tasks } for crew runs.
        var finalOutput = TryGetStringProperty(result, "finalOutput");
        string? summary = finalOutput ?? SafeSerialize(result);
        return new CrewRunOutput { ok = true, summary = summary, artifacts = result };
    }

    private static string? TryGetStringProperty(object obj, string propertyName)
    {
        var prop = obj.GetType().GetProperty(propertyName);
        var value = prop?.GetValue(obj);
        return value?.ToString();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Defensive serialization: any JsonSerializer failure on an arbitrary crew result falls back to ToString so summary rendering can never throw.")]
    private static string? SafeSerialize(object value)
    {
        try { return JsonSerializer.Serialize(value); }
        catch { return value.ToString(); }
    }

    /// <summary>Serializes a JS argument to a JSON string for the <c>inputs</c> global, or null.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Defensive serialization: any JsonSerializer failure on an arbitrary JS input falls back to null so the inputs global is omitted rather than throwing.")]
    private static string? ToInputsJson(JsValue? input)
    {
        if (input is null || input.IsUndefined() || input.IsNull())
            return null;
        var clr = input.ToObject();
        if (clr is null) return null;
        try { return JsonSerializer.Serialize(clr); }
        catch { return null; }
    }

    private static string CombineVirtual(string dir, params string[] segments)
    {
        var trimmed = dir.TrimEnd('/');
        return trimmed + "/" + string.Join("/", segments);
    }

    private static string SanitizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("script-host: a crew name is required.");
        if (name.Contains('/', StringComparison.Ordinal) || name.Contains('\\', StringComparison.Ordinal) || name.Contains("..", StringComparison.Ordinal))
            throw new InvalidOperationException($"script-host: invalid crew name '{name}'.");
        return name.Trim();
    }

    // --- source-generated logging ---

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Crew '{Crew}' run failed.")]
    partial void LogCrewRunFailed(Exception ex, string crew);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug,
        Message = "Could not enumerate crew directory {Dir}; skipping.")]
    partial void LogEnumerateCrewDirFailed(Exception ex, string dir);
}
#pragma warning restore IDE1006
