using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Abstractions.Registry;
using Orkeon.Cli.Registry;
using Orkeon.Cli.Scripting.Registry;
using Orkeon.Domain.FileSystem;

namespace Orkeon.ConsoleApp.Services;

/// <summary>
/// Host-side <see cref="IReplInputAssist"/>: completes <c>/command</c> tokens from the live command
/// registries and <c>@path</c> tokens from the virtual file system (rooted at <c>/workspace</c>).
/// Wired into <see cref="Orkeon.Cli.TerminalGui.Layout.ReplPaneView"/> by the TUI bootstrap.
/// </summary>
internal sealed class ReplInputAssist : IReplInputAssist
{
    /// <summary>Virtual root that a bare <c>@leaf</c> (no slash) is resolved against.</summary>
    private const string WorkspaceRoot = "/workspace";

    /// <summary>Upper bound on entries scanned/returned, so completion stays snappy on large dirs.</summary>
    private const int MaxResults = 50;

    private static readonly TimeSpan ListTimeout = TimeSpan.FromMilliseconds(750);

    private readonly ScriptCommandRegistry _scripts;
    private readonly DefaultCommandRegistry _defaults;
    private readonly IFileSystemService _fs;

    public ReplInputAssist(ScriptCommandRegistry scripts, DefaultCommandRegistry defaults, IFileSystemService fs)
    {
        _scripts = scripts ?? throw new ArgumentNullException(nameof(scripts));
        _defaults = defaults ?? throw new ArgumentNullException(nameof(defaults));
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
    }

    /// <inheritdoc />
    public IReadOnlyList<string> CompleteCommand(string prefix)
    {
        prefix ??= string.Empty;
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var registry in new IInteractiveCommandRegistry[] { _scripts, _defaults })
        {
            foreach (var cmd in registry.Commands)
            {
                AddIfMatch(names, cmd.Name, prefix);
                foreach (var alias in cmd.Aliases)
                    AddIfMatch(names, alias, prefix);
            }
        }
        return names.ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<string> CompletePath(string prefix)
    {
        prefix ??= string.Empty;
        var absolute = prefix.StartsWith('/');
        var basePath = absolute ? prefix : $"{WorkspaceRoot}/{prefix}";

        var lastSlash = basePath.LastIndexOf('/');
        var dir = lastSlash <= 0 ? WorkspaceRoot : basePath[..lastSlash];
        var leaf = basePath[(lastSlash + 1)..];

        var results = new List<string>();
        foreach (var entry in ListChildren(dir))
        {
            var segment = SegmentOf(entry.VirtualPath);
            if (segment.Length == 0 || !segment.StartsWith(leaf, StringComparison.OrdinalIgnoreCase))
                continue;

            var insert = absolute ? entry.VirtualPath : RelativeToWorkspace(entry.VirtualPath);
            if (entry.Kind == VirtualEntryKind.Directory)
                insert += "/";
            results.Add(insert);
        }

        results.Sort(StringComparer.OrdinalIgnoreCase);
        return results.Count > MaxResults ? results.GetRange(0, MaxResults) : results;
    }

    private static void AddIfMatch(SortedSet<string> set, string name, string prefix)
    {
        if (!string.IsNullOrEmpty(name) && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            set.Add(name);
    }

    private static string SegmentOf(string virtualPath)
    {
        var i = virtualPath.LastIndexOf('/');
        return i < 0 ? virtualPath : virtualPath[(i + 1)..];
    }

    private static string RelativeToWorkspace(string virtualPath)
        => virtualPath.StartsWith(WorkspaceRoot + "/", StringComparison.Ordinal)
            ? virtualPath[(WorkspaceRoot.Length + 1)..]
            : virtualPath.TrimStart('/');

    /// <summary>
    /// Synchronously lists the direct children of <paramref name="virtualDir"/>. Bounded by a short
    /// timeout and <see cref="MaxResults"/>; any access denial / missing dir yields no candidates.
    /// Tab runs on the UI thread, so this must never throw or block for long.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort tab-completion on the UI thread: a denied/missing/faulted enumeration yields no candidates and must never throw or block the REPL.")]
    private List<VirtualFileEntry> ListChildren(string virtualDir)
    {
        try
        {
            var task = Task.Run(async () =>
            {
                var list = new List<VirtualFileEntry>();
                var options = new VirtualEnumerationOptions(Recursive: false);
                await foreach (var entry in _fs.EnumerateFilesAsync(virtualDir, options, CancellationToken.None)
                                   .ConfigureAwait(false))
                {
                    list.Add(entry);
                    if (list.Count >= MaxResults * 4)
                        break;
                }
                return list;
            });

            return task.Wait(ListTimeout) ? task.Result : new List<VirtualFileEntry>();
        }
        catch
        {
            // Denied / missing / faulted enumeration → no completions.
            return new List<VirtualFileEntry>();
        }
    }
}
