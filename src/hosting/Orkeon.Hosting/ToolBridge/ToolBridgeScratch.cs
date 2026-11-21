using System.Collections.Immutable;
using Orkeon.Compliance.Vfs;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Hosting.ToolBridge;

/// <summary>
/// The file system of one bridged call (#445, ADR-019): a scratch folder per caller mount, presented at the caller's
/// virtual paths through <see cref="IFileSystemScope"/> for the length of the call, so a path argument the model wrote
/// stays a path of the caller. The files the call sends are placed there first; what the tool wrote there goes back
/// as bytes; the folder is removed with the scratch. The bridge's own agent-facing mounts are out of reach meanwhile,
/// its internal ones are carried forward.
/// </summary>
[SuppressVfsCompliance("EXCEPTION-BOOTSTRAP: creates, fills, reads and removes the scratch folder of one bridged call around the mounts that present it.")]
internal sealed class ToolBridgeScratch : IDisposable
{
    private readonly string _root;
    private readonly FileSystemRegistry _registry;
    private readonly IDisposable _entered;
    private readonly ImmutableList<(BridgeMount Mount, string Folder)> _mounts;
    private readonly Dictionary<string, ReadOnlyMemory<byte>> _sent = new(StringComparer.Ordinal);

    private ToolBridgeScratch(string root, FileSystemRegistry registry, IDisposable entered, ImmutableList<(BridgeMount, string)> mounts)
    {
        _root = root;
        _registry = registry;
        _entered = entered;
        _mounts = mounts;
    }

    /// <summary>
    /// The folder every scratch of this process is made in, under the temporary directory. The bridge's path validator
    /// must allow it, as it allows the sandbox's session folder: <see cref="ToolBridgeStartup.RegisterServing"/> adds it
    /// to <c>PathSecurity:AdditionalAllowedDirectories</c>; without it a tool that writes through the file system (the
    /// screenshot of <c>firefox</c>) is refused with "Path is outside the allowed workspace directory".
    /// </summary>
    public static string Parent { get; } =
        Path.Combine(Path.GetTempPath(), "orkeon-bridge-" + Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Removes <see cref="Parent"/> once no scratch is left in it; nothing when it is absent or still holds one.</summary>
    public static void RemoveParent()
    {
        try
        {
            if (Directory.Exists(Parent) && !Directory.EnumerateFileSystemEntries(Parent).Any())
                Directory.Delete(Parent);
        }
        catch (IOException)
        {
            // A scratch created meanwhile: the folder is left to the system.
        }
        catch (UnauthorizedAccessException)
        {
            // Same.
        }
    }

    /// <summary>
    /// Builds the scratch of <paramref name="call"/>, places its files and enters its mounts for the calling flow.
    /// </summary>
    /// <param name="call">The call, with its mounts.</param>
    /// <param name="scope">The ambient scope the bridge's file system reads.</param>
    /// <param name="boot">The bridge's own mounts, whose internal ones are carried forward.</param>
    /// <returns>The scratch; dispose it once the tool returned.</returns>
    /// <exception cref="ToolBridgeFileException">A file of the call has no place in the caller's mounts.</exception>
    public static ToolBridgeScratch Enter(BridgeCallRequest call, IFileSystemScope scope, FileSystemRegistry boot)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(boot);
        var folder = Path.Combine(Parent, Guid.NewGuid().ToString("N"));
        var root = (OperatingSystem.IsWindows()
            ? Directory.CreateDirectory(folder)
            : Directory.CreateDirectory(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute)).FullName;
        var handedOver = false;
        try
        {
            var mounts = (call.Mounts ?? []).Select((m, i) => (Mount: m, Folder: Directory.CreateDirectory(Path.Combine(root, i.ToString(System.Globalization.CultureInfo.InvariantCulture))).FullName))
                .ToImmutableList();
            var scratch = mounts.Select(m => new FileSystemMount(m.Folder, m.Mount.Path, m.Mount.Writable ? FileAccessRights.ReadWrite : FileAccessRights.ReadOnly)).ToList();
            var claimed = scratch.Select(m => m.VirtualPath).ToHashSet(StringComparer.Ordinal);
            var registry = new FileSystemRegistry(
                [.. scratch, .. boot.GetMounts().Where(m => m.Visibility == MountVisibility.Internal && !claimed.Contains(m.VirtualPath))]);
            Designate(registry, call.WorkingDirectory, call.WriteDirectory);

            var placed = new ToolBridgeScratch(root, registry, scope.Enter(registry), mounts);
            handedOver = true;
            try
            {
                foreach (var file in call.Files ?? [])
                    placed.Place(file);
            }
            catch
            {
                placed.Dispose();
                throw;
            }

            return placed;
        }
        catch when (!handedOver)
        {
            Directory.Delete(root, recursive: true);
            throw;
        }
    }

    /// <summary>
    /// The files the tool wrote in the caller's mounts: each file of the scratch that the call did not send, or whose
    /// content changed.
    /// </summary>
    /// <param name="maxBytes">The largest total.</param>
    /// <param name="cancellationToken">Cancels the reading.</param>
    /// <returns>The files, by the caller's virtual path.</returns>
    /// <exception cref="ToolBridgeFileException">The files exceed <paramref name="maxBytes"/>.</exception>
    public async Task<ImmutableList<BridgeFile>> WrittenAsync(long maxBytes, CancellationToken cancellationToken)
    {
        var written = ImmutableList.CreateBuilder<BridgeFile>();
        long total = 0;
        foreach (var (mount, folder) in _mounts)
        {
            foreach (var physical in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(folder, physical).Replace(Path.DirectorySeparatorChar, '/');
                var virtualPath = mount.Path.TrimEnd('/') + "/" + relative;
                total += new FileInfo(physical).Length;
                if (total > maxBytes)
                    throw new ToolBridgeFileException(
                        $"the files the tool wrote exceed {ToolBridgeServerOptions.SectionName}:MaxFileBytes ({ToolBridgeProtocol.Describe(maxBytes)}).");
                var content = await File.ReadAllBytesAsync(physical, cancellationToken).ConfigureAwait(false);
                if (_sent.TryGetValue(virtualPath, out var sent) && sent.Span.SequenceEqual(content))
                    continue;
                written.Add(new BridgeFile { Path = virtualPath, Content = content });
            }
        }

        return written.ToImmutable();
    }

    /// <summary>Leaves the mounts and removes the scratch folder.</summary>
    public void Dispose()
    {
        _entered.Dispose();
        _registry.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A file still open on a slow platform: the temporary folder is left to the system.
        }
        catch (UnauthorizedAccessException)
        {
            // Same.
        }
    }

    private static void Designate(FileSystemRegistry registry, string? working, string? write)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(working))
                registry.SetWorkingDirectory(working);
            if (!string.IsNullOrWhiteSpace(write))
                registry.SetWriteDirectory(write);
        }
        catch (ArgumentException)
        {
            // A directory outside the mounts sent: relative paths then resolve as the registry's default says.
        }
    }

    private void Place(BridgeFile file)
    {
        string physical;
        try
        {
            physical = _registry.ResolveAndCheckRights(file.Path, FileAccessRights.Read);
        }
        catch (Exception ex) when (ex is FileAccessDeniedException or ArgumentException or FormatException)
        {
            throw new ToolBridgeFileException($"the file '{file.Path}' the caller sent is in none of its mounts.", ex);
        }

        if (!_mounts.Any(m => physical.StartsWith(m.Folder + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            throw new ToolBridgeFileException($"the file '{file.Path}' the caller sent is in none of its mounts.");
        Directory.CreateDirectory(Path.GetDirectoryName(physical)!);
        File.WriteAllBytes(physical, file.Content.Span);
        _sent[file.Path] = file.Content;
    }
}

/// <summary>A file of a bridged call that cannot cross: outside the caller's mounts, or over the size limit.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1032", Justification = "Internal to the bridge: always built with its reason.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1064", Justification = "Internal to the bridge: turned into the call's failure before it leaves the assembly.")]
internal sealed class ToolBridgeFileException : Exception
{
    /// <summary>Builds the failure.</summary>
    /// <param name="message">Its reason.</param>
    /// <param name="inner">The exception behind it, when one.</param>
    public ToolBridgeFileException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
