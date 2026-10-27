using System.Collections.Immutable;
using Jint.Native;
using Orkeon.Cli.Commands.Scripting.Args;

namespace Orkeon.Cli.Commands.Scripting.Loading;

/// <summary>Whether a descriptor came from <c>defineCommand</c> (sync) or <c>defineAsyncCommand</c> (async).</summary>
public enum CommandKind
{
    /// <summary><c>defineCommand</c> — a single <see cref="CommandDescriptor.Handler"/> awaited before the prompt returns.</summary>
    Sync,

    /// <summary><c>defineAsyncCommand</c> — <see cref="CommandDescriptor.Dispatch"/> detaches; <see cref="CommandDescriptor.Completed"/> replays later.</summary>
    Async,
}

/// <summary>
/// Internal descriptor produced by <see cref="CommandDescriptorCollector"/> from a
/// <c>defineCommand({...})</c> call inside a <c>*.cmd.ts</c> script.
/// </summary>
/// <remarks>
/// Phase 1: <see cref="Handler"/> is invoked with <c>{ raw: string[] }</c>. The optional
/// args schema (typed parsing) lands in Phase 3 — the <see cref="ArgsSchemaJson"/> slot
/// is reserved now so the collector contract stays stable across phases.
/// </remarks>
public sealed record CommandDescriptor
{
    /// <summary>Virtual path of the source <c>*.cmd.ts</c> file (used for diagnostics and telemetry).</summary>
    public required string SourceVirtualPath { get; init; }

    /// <summary>Primary command name (validated against <c>^[a-z][a-z0-9-]*$</c>).</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Group declared by the script (<c>group</c>, validated against <c>^[a-z][a-z0-9-]*$</c>), or
    /// <see langword="null"/> when absent; the group then derives from the folder (<see cref="EffectiveGroup"/>).
    /// </summary>
    public string? Group { get; init; }

    /// <summary>
    /// The loader directory that found the script (one of <c>Orkeon:Cli:ScriptCommands:Directories</c>), or
    /// <see langword="null"/> when the descriptor was not loaded from one; the folder group is taken relative to it.
    /// </summary>
    public string? SourceDirectory { get; init; }

    /// <summary>
    /// The declared <see cref="Group"/>, else the name of the folder holding the script, below the
    /// <see cref="SourceDirectory"/> that found it (<c>/cli-commands/session/clear.cmd.ts</c> found under
    /// <c>/cli-commands</c> gives <c>session</c>), when it is a valid group name, else <see langword="null"/> (a file
    /// directly in that directory, whatever its depth in the mount).
    /// </summary>
    public string? EffectiveGroup
    {
        get
        {
            if (Group is not null) return Group;
            var root = (SourceDirectory ?? string.Empty).TrimEnd('/') + "/";
            var relative = SourceVirtualPath.StartsWith(root, StringComparison.Ordinal)
                ? SourceVirtualPath[root.Length..]
                : SourceVirtualPath;
            var segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 2) return null;
            var folder = segments[^2];
            return CommandDescriptorValidator.IsValidGroupName(folder) ? folder : null;
        }
    }

    /// <summary>Alternate identifiers. Must not contain <see cref="Name"/>.</summary>
    public ImmutableArray<string> Aliases { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>Single-line description (&lt;= 200 chars, no newline).</summary>
    public required string Description { get; init; }

    /// <summary>Sync (<c>defineCommand</c>) or async (<c>defineAsyncCommand</c>) — drives the runner aiguillage (design §4, §8 item 8).</summary>
    public CommandKind Kind { get; init; } = CommandKind.Sync;

    /// <summary>JS handler closure for sync commands — captured at script evaluation time. <see langword="null"/> for async.</summary>
    public JsValue? Handler { get; init; }

    /// <summary>JS <c>dispatch(args, ctx)</c> closure for async commands — launches and returns the prompt. <see langword="null"/> for sync.</summary>
    public JsValue? Dispatch { get; init; }

    /// <summary>Optional JS <c>completed(result, ctx)</c> closure for async commands — replayed on the engine thread at the agent's response (design §4.3).</summary>
    public JsValue? Completed { get; init; }

    /// <summary>
    /// Per-command admission quota (design §5). Bounds the number of in-flight instances of
    /// <em>this</em> command. <see langword="null"/> ⇒ unbounded (∞). Only meaningful for async
    /// commands (a sync command holds the engine and is naturally serialised).
    /// </summary>
    public int? MaxConcurrent { get; init; }

    /// <summary>
    /// Raw <c>args</c> JS value when the script declares a schema. Resolved into a typed
    /// <see cref="ArgsSchema"/> by the loader (Phase 3+). <see langword="null"/> here
    /// means the handler will receive <c>{ raw: string[] }</c>.
    /// </summary>
    public JsValue? ArgsSchemaJson { get; init; }

    /// <summary>
    /// Typed args schema parsed from <see cref="ArgsSchemaJson"/>. <see langword="null"/>
    /// or empty when the script doesn't declare <c>args</c> — in that case the handler
    /// receives <c>{ raw: string[] }</c> (spec §4.3).
    /// </summary>
    public ImmutableArray<ArgSpec> ArgsSchema { get; init; } = ImmutableArray<ArgSpec>.Empty;

    /// <summary>True when the descriptor declares a typed args schema (drives ScriptCommand parsing branch).</summary>
    public bool HasArgsSchema => !ArgsSchema.IsDefaultOrEmpty && ArgsSchema.Length > 0;
}
