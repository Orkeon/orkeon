using System.Collections.Immutable;

namespace Orkeon.Cli.Commands.Scripting.Loading;

/// <summary>
/// Phase 1 options for <see cref="ScriptCommandLoader"/>. Phase 4 promotes these to a
/// fully bound <c>ScriptCommandsConfiguration</c> with appsettings support, CLI flags,
/// and sandbox limits.
/// </summary>
/// <remarks>
/// Defaults follow spec §6.4. Directories are <em>virtual</em> paths resolved through
/// <see cref="Orkeon.Domain.FileSystem.IFileSystemService"/> — the loader does NOT touch
/// the disk directly (cf. VFS rule, CLAUDE.md).
/// </remarks>
public sealed record ScriptCommandLoaderOptions
{
    /// <summary>Master switch. When false, the loader returns an empty registry.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Virtual paths of directories to scan for <c>*.cmd.ts</c> files. Order is significant:
    /// in case of name collision, the first directory wins (spec §8.4).
    /// </summary>
    public ImmutableArray<string> Directories { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>Glob applied to <see cref="Orkeon.Domain.FileSystem.VirtualEnumerationOptions.SearchPattern"/>.</summary>
    public string SearchPattern { get; init; } = "*.cmd.ts";

    /// <summary>Hard cap on the number of engines kept in cache (spec §6.4 / §15).</summary>
    public int MaxScripts { get; init; } = 50;

    /// <summary>When true, the first invalid script aborts loader startup (CI pipelines).</summary>
    public bool FailFastOnInvalidScript { get; init; }

    /// <summary>When true, two scripts declaring the same command name → first wins, second logged.</summary>
    public bool ContinueOnConflict { get; init; } = true;

    /// <summary>Run <c>esbuild</c> on each <c>*.cmd.ts</c> source. False = expect pre-transpiled JS (test/debug only).</summary>
    public bool EsbuildTranspile { get; init; } = true;
}
