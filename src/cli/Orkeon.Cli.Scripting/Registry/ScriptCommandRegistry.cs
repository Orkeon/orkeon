using System.Collections.Immutable;
using Orkeon.Cli.Abstractions.Commands;
using Orkeon.Cli.Abstractions.Registry;
using Orkeon.Cli.Scripting.Loading;
using Orkeon.Cli.Scripting.Runtime;

namespace Orkeon.Cli.Scripting.Registry;

/// <summary>
/// <see cref="IInteractiveCommandRegistry"/> populated by <c>ScriptCommandLoader</c>.
/// Holds a frozen snapshot of <see cref="ScriptCommand"/> instances; commands are
/// discovered once per process (no hot-reload — cf. spec §14).
/// </summary>
/// <remarks>
/// <para>
/// Registered in DI by its <em>concrete type</em> (see plan Q6) so it can coexist with
/// the host's other registries (MainMenu, Qa, …). The demo runner depends on
/// <see cref="ScriptCommandRegistry"/> directly, not on <see cref="IInteractiveCommandRegistry"/>.
/// </para>
/// <para>
/// Phase 3 addition: the registry auto-appends a <see cref="ScriptHelpCommand"/> so users
/// can type <c>help-cmd &lt;name&gt;</c> to see typed args.
/// </para>
/// <para>
/// R10.3 / ANT-002 — two construction modes. The <em>snapshot</em> constructor (used by the
/// loader and by tests) is fully loaded up front. The <em>deferred</em> constructor (used by
/// the DI registration) only captures a load callback: script discovery, esbuild transpilation,
/// and Jint evaluation run at the first <see cref="EnsureLoadedAsync"/> call — never during DI
/// resolution. Until that first load completes, the registry exposes no commands.
/// </para>
/// </remarks>
public sealed class ScriptCommandRegistry : IInteractiveCommandRegistry
{
    private readonly ImmutableArray<ScriptCommand> _scripts;
    private readonly ImmutableArray<IInteractiveCommand> _all;

    // Deferred-loading state (R10.3 / ANT-002). _loaded flips at most once per successful load,
    // from null to the snapshot registry produced by _loadAsync; sync members read it lock-free.
    private readonly Func<CancellationToken, Task<(ScriptCommandRegistry Registry, CommandLoadResult Summary)>>? _loadAsync;
    private readonly object _gate = new();
    private Task<(ScriptCommandRegistry Registry, CommandLoadResult Summary)>? _load;
    private volatile ScriptCommandRegistry? _loaded;

    /// <summary>Snapshot mode: the registry is fully loaded at construction time.</summary>
    public ScriptCommandRegistry(IEnumerable<ScriptCommand> commands, IEnumerable<IInteractiveCommand>? builtins = null)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _scripts = commands.ToImmutableArray();

        // The companion help-cmd surfaces ArgsSchema details without touching Orkeon.Cli.
        var helper = new ScriptHelpCommand(() => _scripts);

        // Dispatch built-ins (ps/inspect/result/cancel) first so they win name resolution over
        // any same-named scripted command; then scripted commands; then the help-cmd helper.
        _all = (builtins ?? Array.Empty<IInteractiveCommand>())
            .Concat(_scripts.Cast<IInteractiveCommand>())
            .Concat(new IInteractiveCommand[] { helper })
            .ToImmutableArray();

        // Snapshot mode is born loaded: EnsureLoadedAsync is a cheap no-op returning a
        // synthetic summary (the loader hands real summaries out alongside this instance).
        _loaded = this;
        _load = Task.FromResult((this, new CommandLoadResult(_scripts.Length, 0, 0, ImmutableArray<string>.Empty)));
    }

    /// <summary>
    /// Deferred mode (R10.3 / ANT-002): captures <paramref name="loadAsync"/> without invoking
    /// it. The load is memoised — triggered by the first <see cref="EnsureLoadedAsync"/> call
    /// and shared by every subsequent caller.
    /// </summary>
    public ScriptCommandRegistry(
        Func<CancellationToken, Task<(ScriptCommandRegistry Registry, CommandLoadResult Summary)>> loadAsync)
    {
        ArgumentNullException.ThrowIfNull(loadAsync);
        _loadAsync = loadAsync;
        _scripts = ImmutableArray<ScriptCommand>.Empty;
        _all = ImmutableArray<IInteractiveCommand>.Empty;
    }

    /// <inheritdoc />
    public IEnumerable<IInteractiveCommand> Commands
        => _loaded is { } loaded ? loaded._all : ImmutableArray<IInteractiveCommand>.Empty;

    /// <inheritdoc />
    public IInteractiveCommand? Fallback => null;

    /// <summary>Number of <em>scripted</em> commands loaded (excludes the builtin help-cmd helper). 0 until loaded.</summary>
    public int Count => _loaded is { } loaded ? loaded._scripts.Length : 0;

    /// <summary>Direct access to the scripted commands — used by integration tests and the demo runner. Empty until loaded.</summary>
    public IReadOnlyList<ScriptCommand> ScriptCommands
        => _loaded is { } loaded ? loaded._scripts : ImmutableArray<ScriptCommand>.Empty;

    /// <summary>True once the command snapshot is available (always true in snapshot mode).</summary>
    public bool IsLoaded => _loaded is not null;

    /// <summary>
    /// Triggers (or joins) the memoised script load and returns its summary; cheap once loaded.
    /// A failed or cancelled load is not cached — the next call retries, mirroring the previous
    /// behaviour where a throwing DI singleton factory was simply re-run on the next resolution,
    /// so script errors keep surfacing to the caller.
    /// </summary>
    public async Task<CommandLoadResult> EnsureLoadedAsync(CancellationToken ct = default)
    {
        Task<(ScriptCommandRegistry Registry, CommandLoadResult Summary)> load;
        lock (_gate)
        {
            if (_load is null || (_load.IsCompleted && !_load.IsCompletedSuccessfully))
                _load = LoadCoreAsync(ct);
            load = _load;
        }

        var (_, summary) = await load.ConfigureAwait(false);
        return summary;
    }

    private async Task<(ScriptCommandRegistry Registry, CommandLoadResult Summary)> LoadCoreAsync(CancellationToken ct)
    {
        // Only reachable in deferred mode: the snapshot constructor pre-completes _load.
        var result = await _loadAsync!(ct).ConfigureAwait(false);
        _loaded = result.Registry;
        return result;
    }
}
