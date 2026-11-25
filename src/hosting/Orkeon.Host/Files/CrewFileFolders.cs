using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Constants.FileSystem;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.FileSystem;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.FileSystem;

namespace Orkeon.Host.Files;

/// <summary>The folders a crew opens, <c>/outbox</c> apart, which no declaration opens or closes.</summary>
/// <param name="Open">The folders; empty when none is open.</param>
/// <param name="Reason">Why none is open; null when one is.</param>
/// <param name="Invalid">Whether that is a fault of the declaration, rather than the want of one.</param>
internal sealed record OpenedFolders(IReadOnlyList<CrewFileFolder> Open, string? Reason = null, bool Invalid = false);

/// <summary>
/// Which folders of a crew people may take files from, or leave files in. The answer comes from the declaration alone, read
/// again at each call: loading a crew would register its tools, and evaluating a script would run it.
/// </summary>
/// <remarks>
/// What is answered here is the declaration, held to the mounts and their rights. Whoever opens a file still goes through
/// the virtual file system, in the mount set of the crew: a right can be narrower under an opened folder than on it.
/// </remarks>
internal sealed partial class CrewFileFolders
{
    private readonly OrkeonHostOptions _options;
    private readonly HostCrewMountPlan _plan;
    private readonly FileSystemRegistry _boot;
    private readonly IFileSystemService _fs;
    private readonly ILogger<CrewFileFolders> _logger;
    private readonly ConcurrentDictionary<string, Parsed> _parsed = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _warned = new(StringComparer.OrdinalIgnoreCase);

    public CrewFileFolders(
        IOptions<OrkeonHostOptions> options,
        HostCrewMountPlan plan,
        FileSystemRegistry boot,
        IFileSystemService fileSystem,
        ILogger<CrewFileFolders> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _boot = boot ?? throw new ArgumentNullException(nameof(boot));
        _fs = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>The folders the crew opens now.</summary>
    /// <param name="crewName">The name the host serves the crew under.</param>
    /// <param name="ct">Cancels the reading.</param>
    public async Task<OpenedFolders> ReadAsync(string crewName, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(crewName);
        var crew = _options.Crews.FirstOrDefault(c => string.Equals(c.Name, crewName, StringComparison.OrdinalIgnoreCase));
        if (crew is null || !_plan.VirtualPaths.TryGetValue(crew.Name, out var path))
            return new OpenedFolders([], $"no crew '{crewName}' is hosted here");

        var opened = await ReadAsync(crew, path, ct).ConfigureAwait(false);
        if (!opened.Invalid)
        {
            _warned.TryRemove(crew.Name, out _);
        }
        else if (!_warned.TryGetValue(crew.Name, out var known) || known != opened.Reason)
        {
            _warned[crew.Name] = opened.Reason!;
            LogClosed(crew.Name, opened.Reason!);
        }

        return opened;
    }

    private async Task<OpenedFolders> ReadAsync(HostedCrewOptions crew, string path, CancellationToken ct)
    {
        try
        {
            var (text, file, reason) = await FindAsync(path, ct).ConfigureAwait(false);
            if (text is null)
                return new OpenedFolders([], reason);

            var declaration = Parse(crew.Name, text, file);
            if (declaration.Fault is { } fault)
                return new OpenedFolders([], fault, Invalid: true);
            if (declaration.Folders is not { Count: > 0 } folders)
                return new OpenedFolders([], "the crew opens no folder: it has no 'files:' block");

            return Refusal(crew, folders) is { } refusal
                ? new OpenedFolders([], refusal, Invalid: true)
                : new OpenedFolders(folders);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileAccessDeniedException)
        {
            return new OpenedFolders([], $"the crew's declaration cannot be read: {Redact(ex, path)}", Invalid: true);
        }
    }

    // An access refusal is worded with virtual paths; what the system says holds the operator's.
    private static string Redact(Exception ex, string path) =>
        ex is FileAccessDeniedException ? ex.Message : $"'{path}' ({ex.GetType().Name})";

    /// <summary>The text that declares the crew's folders, or why there is none. A YAML crew's neighbour is not read.</summary>
    private async Task<(string? Text, string File, string? Reason)> FindAsync(string path, CancellationToken ct)
    {
        if (CrewFilesBlock.NeighbourOf(path) is { } neighbour)
        {
            return (await _fs.TryReadAllTextAsync(neighbour, ct).ConfigureAwait(false), neighbour,
                $"the crew opens no folder: a script declares them in '{Path.GetFileName(neighbour)}', beside it, and there is none");
        }

        if (!_plan.Roots.Contains(path, StringComparer.Ordinal))
            return (await _fs.TryReadAllTextAsync(path, ct).ConfigureAwait(false), path, $"'{path}' is not there");

        var settings = $"{path}/{ConventionalNames.CrewSettingsFile}";
        if (await _fs.TryReadAllTextAsync(settings, ct).ConfigureAwait(false) is { } text)
            return (text, settings, null);

        var fallback = $"{path}/{ConventionalNames.CrewSettingsFallbackFile}";
        return (await _fs.TryReadAllTextAsync(fallback, ct).ConfigureAwait(false), fallback,
            $"'{settings}' is not there, nor '{fallback}'");
    }

    // The text decides, not the date of the file: a date can stand still while the text changes.
    private CrewFilesBlock.Declaration Parse(string crew, string text, string file)
    {
        if (_parsed.TryGetValue(crew, out var known) && known.File == file && known.Text == text)
            return known.Declaration;

        var declaration = CrewFilesBlock.NeighbourOf(_plan.VirtualPaths[crew]) is null
            ? CrewFilesBlock.ReadCrew(text, file)
            : CrewFilesBlock.ReadNeighbour(text, file);
        _parsed[crew] = new Parsed(file, text, declaration);
        return declaration;
    }

    private string? Refusal(HostedCrewOptions crew, IReadOnlyList<CrewFileFolder> folders)
    {
        using var registry = ScopedMountComposition.ForExecution(_boot, CrewRunner.GrantedMounts(crew));
        foreach (var folder in folders)
        {
            if (Refusal(registry, folder) is { } refusal)
                return $"files: '{folder.Path}' is refused: {refusal}";
        }

        return null;
    }

    private string? Refusal(FileSystemRegistry registry, CrewFileFolder folder)
    {
        if (Claims(folder.Path, RunnerVirtualRoots.Outbox))
            return $"{RunnerVirtualRoots.Outbox} belongs to the conversation, and is always open to download";
        if (Claims(folder.Path, RunnerVirtualRoots.Inbox))
            return $"{RunnerVirtualRoots.Inbox} belongs to the conversation";
        if (CrewsRoot().IsMatch(folder.Path))
            return $"{HostCrewMounts.VirtualPathPrefix} holds the crews' definitions";

        if (folder.Download && Denial(registry, folder.Path, FileAccessRights.Read) is { } unread)
            return unread;
        if (folder.Upload && Denial(registry, folder.Path, FileAccessRights.Write | FileAccessRights.Create) is { } unwritten)
            return unwritten;

        return ReachesACrew(registry, folder) ? "it reaches the folder of a crew's definition" : null;
    }

    private static string? Denial(FileSystemRegistry registry, string path, FileAccessRights right)
    {
        try
        {
            registry.ResolveAndCheckRights(path, right);
        }
        catch (FileAccessDeniedException ex)
        {
            return ex.Message;
        }

        // A file system may fold case where the mount's overrides do not: /output/Private is bound by private:ro.
        var mount = registry.GetMounts()
            .Where(m => path == m.VirtualPath || path.StartsWith(m.VirtualPath + "/", StringComparison.Ordinal))
            .OrderByDescending(m => m.VirtualPath.Length)
            .First();
        var relative = path[mount.VirtualPath.Length..].TrimStart('/');
        foreach (var bound in mount.Overrides)
        {
            if (Claims(relative, bound.RelativePath))
                return bound.Rights.HasFlag(right) ? null : $"the mount of '{mount.VirtualPath}' does not grant {right} on '{bound.RelativePath}'";
        }

        return null;
    }

    /// <summary>
    /// Whether the folder is a second address for what <c>/crews</c> holds: it is or holds the folder of a crew, or is
    /// part of a crew directory. As written and as the links lead; in any case where the file system ignores it.
    /// </summary>
    [Orkeon.Compliance.Vfs.SuppressVfsCompliance("OUT-OF-SCOPE: compares the folders two mounts of the operator lead to, and opens nothing; every path was resolved by the registry first.")]
    private bool ReachesACrew(FileSystemRegistry registry, CrewFileFolder opened)
    {
        // The rights were checked; the physical folder is what is asked for here.
        var folder = registry.ResolveAndCheckRights(
            opened.Path, opened.Download ? FileAccessRights.Read : FileAccessRights.Write | FileAccessRights.Create);
        string[] folders = [folder, PhysicalRealPath.Resolve(folder) ?? folder];
        var directories = _plan.VirtualPaths.Values.ToHashSet(StringComparer.Ordinal);
        foreach (var mount in _boot.GetMounts().Where(m => _plan.Roots.Contains(m.VirtualPath, StringComparer.Ordinal)))
        {
            var crews = Path.GetFullPath(mount.BasePath);
            string[] roots = [crews, PhysicalRealPath.Resolve(crews) ?? crews];
            var whole = directories.Contains(mount.VirtualPath);
            if (roots.Any(root => folders.Any(f =>
                    PhysicalOverlap.IsUnder(root, f) || (whole && PhysicalOverlap.IsUnder(f, root)))))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Claims(string path, string root) =>
        string.Equals(path, root, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"^/crews(-[0-9]+)?(/|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CrewsRoot();

    private sealed record Parsed(string File, string Text, CrewFilesBlock.Declaration Declaration);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The folders of crew '{Crew}' are closed: {Reason}")]
    private partial void LogClosed(string crew, string reason);
}
