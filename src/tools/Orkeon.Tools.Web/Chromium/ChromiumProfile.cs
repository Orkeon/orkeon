using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Orkeon.Compliance.Vfs;
using Orkeon.Tools.Web.Browser;
using Orkeon.Tools.Web.Firefox;

namespace Orkeon.Tools.Web.Chromium;

/// <summary>
/// The user data folder of one Chromium browser. A disposable one is created for the browser and removed
/// with it; any other is a folder the operator points at, used in place and never removed. The user's own
/// folder is never one: Chromium browsers refuse the remote protocol on it.
/// </summary>
[SuppressVfsCompliance("OUT-OF-SCOPE: the browser's own data folders on the host, outside every mount; the browser, not the framework, reads and writes there.")]
internal sealed class ChromiumProfile
{
    /// <summary>The file that marks a folder as a disposable one of the tool's.</summary>
    internal const string Marker = ".orkeon-browser";

    /// <summary>How old a disposable folder nothing holds must be to count as left behind.</summary>
    internal static readonly TimeSpan StaleAge = TimeSpan.FromMinutes(10);

    // A fresh folder's only settings: the browser offers to save no password.
    private const string Preferences = """{"credentials_enable_service":false,"profile":{"password_manager_enabled":false}}""";

    private ChromiumProfile(string directory, bool disposable)
    {
        Directory = directory;
        Disposable = disposable;
    }

    /// <summary>Physical path of the folder, handed to the browser as <c>--user-data-dir</c>.</summary>
    public string Directory { get; }

    /// <summary><c>true</c> for a folder created for one browser and removed with it.</summary>
    public bool Disposable { get; }

    /// <summary>
    /// Where disposable folders go when the options name none: the snap's own folder when the browser is a
    /// snap, else a folder of the system's temp folder.
    /// </summary>
    public static string DefaultRoot(ChromiumVariant variant, string? home, string tempPath, Func<string, bool> directoryExists)
    {
        ArgumentNullException.ThrowIfNull(variant);
        ArgumentNullException.ThrowIfNull(directoryExists);
        if (variant.Snap is { } snap && !string.IsNullOrEmpty(home))
        {
            var common = Path.Combine(home, "snap", snap, "common");
            if (directoryExists(common))
                return Path.Combine(common, "orkeon", "profiles");
        }

        return Path.Combine(tempPath, "orkeon-" + variant.Id);
    }

    /// <summary>The root of the options, or the default one of this machine.</summary>
    public static string ResolveRoot(ChromiumVariant variant, string? configured) =>
        string.IsNullOrWhiteSpace(configured)
            ? DefaultRoot(variant, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Path.GetTempPath(), System.IO.Directory.Exists)
            : Path.GetFullPath(configured);

    /// <summary>Creates a disposable folder under <paramref name="root"/>, once what a killed host left there is removed.</summary>
    /// <exception cref="FirefoxException">The folder could not be created.</exception>
    public static ChromiumProfile Create(string root)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        RemoveStale(root, StaleAge);

        // OUT-OF-SCOPE: host folder of the browser process, not a VFS path.
        var directory = Path.Combine(root, Guid.NewGuid().ToString("N"));
        try
        {
            System.IO.Directory.CreateDirectory(Path.Combine(directory, "Default"));
            File.WriteAllText(Path.Combine(directory, Marker), string.Empty);
            File.WriteAllText(Path.Combine(directory, "Default", "Preferences"), Preferences);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new FirefoxException($"The browser's profile folder could not be created ({ex.Message}).", ex);
        }

        return new ChromiumProfile(directory, disposable: true);
    }

    /// <summary>The data folder <paramref name="name"/> points at, used in place.</summary>
    /// <param name="displayName">The browser's name, for the errors.</param>
    /// <param name="name">The path of the folder.</param>
    /// <param name="userFolders">The user's own data folders, refused.</param>
    /// <exception cref="FirefoxException">A profile name rather than a folder, a folder of the user's own browser, one that does not exist or that another browser holds.</exception>
    public static ChromiumProfile Open(string displayName, string name, IReadOnlyList<string> userFolders)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(userFolders);

        if (!Path.IsPathRooted(name))
        {
            throw new FirefoxException(
                $"'{name}' was refused: {displayName} does not open the profiles of its own user to a remote protocol. Name a profile by the full path of a data folder kept for it, or use '{BrowserProfileChoice.DisposableName}'.");
        }

        var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(name));
        if (userFolders.Any(folder => Holds(Path.TrimEndingDirectorySeparator(folder), directory)))
        {
            throw new FirefoxException(
                $"'{name}' was refused: it is the data folder of the user's own {displayName}, which does not open it to a remote protocol.");
        }

        // OUT-OF-SCOPE: a data folder of the host's browser, not a VFS path.
        if (!System.IO.Directory.Exists(directory))
            throw new FirefoxException($"The profile folder '{directory}' does not exist.");
        if (LockedBy(directory) is { } holder)
            throw new FirefoxException($"The profile '{name}' is open in another {displayName} ({holder}): close it, or use another profile.");
        return new ChromiumProfile(directory, disposable: false);
    }

    /// <summary>The id of the process a <c>SingletonLock</c> names (<c>host-pid</c>), or <c>null</c>.</summary>
    public static int? LockHolder(string? linkTarget)
    {
        if (string.IsNullOrEmpty(linkTarget))
            return null;
        var dash = linkTarget.LastIndexOf('-');
        return dash >= 0 && int.TryParse(linkTarget[(dash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var pid) && pid > 0
            ? pid
            : null;
    }

    /// <summary>
    /// Removes the disposable folders under <paramref name="root"/> that no browser holds and that are older
    /// than <paramref name="minimumAge"/>. A folder without the marker is not the tool's. Best effort.
    /// </summary>
    [SuppressMessage("Design", "CA1031", Justification = "Best-effort cleanup: a folder that cannot be read or removed is left for the next start.")]
    public static void RemoveStale(string root, TimeSpan minimumAge)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);

        // OUT-OF-SCOPE: host folder of the browser process, not a VFS path.
        try
        {
            if (!System.IO.Directory.Exists(root))
                return;
            var cutoff = DateTime.UtcNow - minimumAge;
            foreach (var directory in System.IO.Directory.EnumerateDirectories(root))
            {
                if (!File.Exists(Path.Combine(directory, Marker))
                    || System.IO.Directory.GetLastWriteTimeUtc(directory) > cutoff
                    || LockedBy(directory) is not null)
                {
                    continue;
                }

                try
                {
                    System.IO.Directory.Delete(directory, recursive: true);
                }
                catch (Exception)
                {
                    // Held for an instant, or not ours to remove: left for the next start.
                }
            }
        }
        catch (Exception)
        {
            // A root that cannot be listed has nothing to clean.
        }
    }

    /// <summary>Removes a disposable folder once the browser has ended; any other is left as it is. Best effort.</summary>
    [SuppressMessage("Design", "CA1031", Justification = "Best-effort cleanup: a folder left behind is only the files of a closed browser.")]
    public async Task RemoveAsync()
    {
        if (!Disposable)
            return;

        // OUT-OF-SCOPE: host folder of the browser process, not a VFS path. The helpers of a browser that
        // has just ended may still write for an instant: a few tries, then the next start's sweep.
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                if (System.IO.Directory.Exists(Directory))
                    System.IO.Directory.Delete(Directory, recursive: true);
                return;
            }
            catch (Exception)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt)).ConfigureAwait(false);
            }
        }
    }

    private static bool Holds(string folder, string path)
    {
        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return string.Equals(folder, path, comparison) || path.StartsWith(folder + Path.DirectorySeparatorChar, comparison);
    }

    /// <summary>What holds the folder open, or <c>null</c>: the process its <c>SingletonLock</c> link names (Unix), or a <c>lockfile</c> held open (Windows).</summary>
    [SuppressMessage("Design", "CA1031", Justification = "A lock that cannot be read is treated as free: the browser decides at start.")]
    private static string? LockedBy(string directory)
    {
        // OUT-OF-SCOPE: the lock of the host's browser.
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var file = Path.Combine(directory, "lockfile");
                if (!File.Exists(file))
                    return null;
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None);
                return null;
            }

            if (LockHolder(new FileInfo(Path.Combine(directory, "SingletonLock")).LinkTarget) is not { } pid)
                return null;
            using var process = Process.GetProcessById(pid);
            return process.HasExited ? null : $"process {pid}";
        }
        catch (IOException) when (OperatingSystem.IsWindows())
        {
            return "another process";
        }
        catch (Exception)
        {
            // No such process (a stale lock), or a lock that cannot be read.
            return null;
        }
    }
}
