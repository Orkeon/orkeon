using Orkeon.Compliance.Vfs;

namespace Orkeon.Studio.Core.FileSystem;

/// <summary>
/// What a folder of the session holds, and word when that changes: the create-a-team wizard
/// counts the files the user drops in the folders kept inside the team while the engine
/// waits on them (refonte, « drop files » state). An interface so the count and the change
/// can be exercised without touching the real disk.
/// </summary>
public interface IFolderWatch
{
    /// <summary>The files under <paramref name="directory"/>, sub-folders included; 0 when it does not exist.</summary>
    int CountFiles(string directory);

    /// <summary>
    /// Calls <paramref name="changed"/> whenever something under <paramref name="directory"/> is
    /// created, deleted or renamed, on any thread, until the returned handle is disposed.
    /// </summary>
    IDisposable Watch(string directory, Action changed);
}

/// <summary>Real-disk <see cref="IFolderWatch"/>.</summary>
[SuppressVfsCompliance(
    "EXCEPTION-BOOTSTRAP: Studio is a host application; the folders it watches are the forge " +
    "session's own directories on this machine, the ones the user drops files in from the OS explorer.")]
public sealed class PhysicalFolderWatch : IFolderWatch
{
    /// <summary>Shared stateless instance.</summary>
    public static PhysicalFolderWatch Instance { get; } = new();

    /// <inheritdoc />
    public int CountFiles(string directory)
    {
        try
        {
            return Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Count()
                : 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <inheritdoc />
    public IDisposable Watch(string directory, Action changed)
    {
        ArgumentNullException.ThrowIfNull(changed);
        if (!Directory.Exists(directory))
            return NoWatch.Instance;

        try
        {
            var watcher = new FileSystemWatcher(directory)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
            };
            watcher.Created += (_, _) => changed();
            watcher.Deleted += (_, _) => changed();
            watcher.Renamed += (_, _) => changed();
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return NoWatch.Instance;
        }
    }

    private sealed class NoWatch : IDisposable
    {
        public static NoWatch Instance { get; } = new();

        public void Dispose()
        {
            // Nothing is watched: a folder that does not exist yet has nothing to report.
        }
    }
}
