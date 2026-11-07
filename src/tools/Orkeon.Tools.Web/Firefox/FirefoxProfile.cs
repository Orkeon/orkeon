using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Orkeon.Compliance.Vfs;

namespace Orkeon.Tools.Web.Firefox;

/// <summary>
/// The profile folder of one browser. A disposable one is created empty with the preferences below and
/// removed when the browser closes; a named one is a profile of the Firefox installed on the host
/// (<c>profiles.ini</c>) or a folder the operator points at, used in place with what it holds and never
/// removed. A browser never shares the user's own profile unless the host names it, and two browsers
/// never hold one profile at once: Firefox locks it.
/// </summary>
[SuppressVfsCompliance("OUT-OF-SCOPE: the browser's own profile folders on the host, outside every mount; Firefox, not the framework, reads and writes there.")]
internal sealed class FirefoxProfile
{
    /// <summary>The folder a Firefox snap may write under the user's home: its temp folder is private.</summary>
    internal const string SnapCommon = "snap/firefox/common";

    /// <summary>
    /// How old a disposable folder no Firefox holds must be to count as left behind: a browser locks its
    /// folder within its startup timeout, so a younger one may be starting still.
    /// </summary>
    internal static readonly TimeSpan StaleAge = TimeSpan.FromMinutes(10);

    // remote.active-protocols 1: WebDriver BiDi alone, no CDP. The others keep a fresh profile quiet:
    // no first-run pages, no default-browser question, no telemetry, no update, no saved password.
    internal const string Preferences = """
        user_pref("remote.active-protocols", 1);
        user_pref("browser.shell.checkDefaultBrowser", false);
        user_pref("browser.aboutwelcome.enabled", false);
        user_pref("browser.preonboarding.enabled", false);
        user_pref("browser.startup.page", 0);
        user_pref("browser.startup.homepage_override.mstone", "ignore");
        user_pref("startup.homepage_welcome_url", "about:blank");
        user_pref("startup.homepage_welcome_url.additional", "");
        user_pref("browser.sessionstore.resume_from_crash", false);
        user_pref("browser.tabs.warnOnClose", false);
        user_pref("browser.warnOnQuit", false);
        user_pref("datareporting.policy.dataSubmissionEnabled", false);
        user_pref("datareporting.healthreport.uploadEnabled", false);
        user_pref("toolkit.telemetry.reportingpolicy.firstRun", false);
        user_pref("app.update.auto", false);
        user_pref("app.normandy.enabled", false);
        user_pref("extensions.update.enabled", false);
        user_pref("signon.rememberSignons", false);

        """;

    private FirefoxProfile(string name, string directory, bool disposable)
    {
        Name = name;
        Directory = directory;
        Disposable = disposable;
    }

    /// <summary>The name of the profile: <c>disposable</c>, or the one the host named.</summary>
    public string Name { get; }

    /// <summary>Physical path of the profile folder, handed to Firefox as <c>--profile</c>.</summary>
    public string Directory { get; }

    /// <summary><c>true</c> for a profile created for one browser and removed with it.</summary>
    public bool Disposable { get; }

    /// <summary>
    /// Where disposable profiles go when the options name no folder: under the snap's own folder when
    /// Firefox is a snap, else under the system's temp folder.
    /// </summary>
    public static string DefaultRoot(string? home, string tempPath, Func<string, bool> directoryExists)
    {
        ArgumentNullException.ThrowIfNull(directoryExists);
        if (!string.IsNullOrEmpty(home))
        {
            var snap = Path.Combine(home, SnapCommon);
            if (directoryExists(snap))
                return Path.Combine(snap, "orkeon", "profiles");
        }

        return Path.Combine(tempPath, "orkeon-firefox");
    }

    /// <summary>The root of the options, or the default one of this machine.</summary>
    public static string ResolveRoot(string? configured)
    {
        // OUT-OF-SCOPE: host probe of the browser's install kind, not a VFS mount.
        return string.IsNullOrWhiteSpace(configured)
            ? DefaultRoot(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Path.GetTempPath(), System.IO.Directory.Exists)
            : Path.GetFullPath(configured);
    }

    /// <summary>
    /// The <c>profiles.ini</c> files the host's Firefox may keep, in the order they are read: the snap's
    /// under Linux, then the platform's usual folder.
    /// </summary>
    public static IReadOnlyList<string> ProfilesIniCandidates(string? home, string? appData, bool isWindows, bool isMacOS)
    {
        List<string> candidates = [];
        if (isWindows)
        {
            if (!string.IsNullOrEmpty(appData))
                candidates.Add(Path.Combine(appData, "Mozilla", "Firefox", "profiles.ini"));
            return candidates;
        }

        if (string.IsNullOrEmpty(home))
            return candidates;
        if (isMacOS)
        {
            candidates.Add(Path.Combine(home, "Library", "Application Support", "Firefox", "profiles.ini"));
            return candidates;
        }

        candidates.Add(Path.Combine(home, SnapCommon, ".mozilla", "firefox", "profiles.ini"));
        candidates.Add(Path.Combine(home, ".mozilla", "firefox", "profiles.ini"));
        return candidates;
    }

    /// <summary>
    /// The folder of the profile <paramref name="name"/> in a <c>profiles.ini</c> (<c>[ProfileN]</c>
    /// sections with <c>Name</c>, <c>IsRelative</c> and <c>Path</c>), or <c>null</c> when it names none.
    /// </summary>
    public static string? FindInProfilesIni(string ini, string iniFolder, string name)
    {
        ArgumentNullException.ThrowIfNull(ini);
        ArgumentException.ThrowIfNullOrEmpty(iniFolder);
        ArgumentException.ThrowIfNullOrEmpty(name);

        string? sectionName = null, path = null;
        var relative = true;
        foreach (var line in ini.Split('\n').Select(raw => raw.Trim()))
        {
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                if (Matches(sectionName, path, relative, iniFolder, name) is { } found)
                    return found;
                sectionName = null;
                path = null;
                relative = true;
                continue;
            }

            var separator = line.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
                continue;
            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (key.Equals("Name", StringComparison.OrdinalIgnoreCase))
                sectionName = value;
            else if (key.Equals("Path", StringComparison.OrdinalIgnoreCase))
                path = value;
            else if (key.Equals("IsRelative", StringComparison.OrdinalIgnoreCase))
                relative = value != "0";
        }

        return Matches(sectionName, path, relative, iniFolder, name);
    }

    /// <summary>The id of the process holding a Unix profile lock (<c>host:+pid</c>), or <c>null</c>.</summary>
    public static int? LockHolder(string? linkTarget)
    {
        if (string.IsNullOrEmpty(linkTarget))
            return null;
        var plus = linkTarget.LastIndexOf('+');
        return plus >= 0 && int.TryParse(linkTarget[(plus + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid) && pid > 0
            ? pid
            : null;
    }

    /// <summary>
    /// Creates a new disposable profile folder under <paramref name="root"/>, once the folders a browser
    /// no longer holds there - what a host that was killed left behind - are removed.
    /// </summary>
    /// <exception cref="FirefoxException">The folder could not be created.</exception>
    public static FirefoxProfile Create(string root)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        RemoveStale(root, StaleAge);

        // OUT-OF-SCOPE: host folder of the browser process, not a VFS path.
        var directory = Path.Combine(root, Guid.NewGuid().ToString("N"));
        try
        {
            System.IO.Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "user.js"), Preferences);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new FirefoxException($"The browser's profile folder could not be created ({ex.Message}).", ex);
        }

        return new FirefoxProfile(FirefoxProfileChoice.DisposableName, directory, disposable: true);
    }

    /// <summary>
    /// The named profile of the host's Firefox (<c>profiles.ini</c>), or the folder <paramref name="name"/>
    /// points at, used in place.
    /// </summary>
    /// <exception cref="FirefoxException">No such profile, or another Firefox holds it open.</exception>
    public static FirefoxProfile Open(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        // OUT-OF-SCOPE: the profile folders of the Firefox installed on the host, not VFS paths.
        var directory = Path.IsPathRooted(name) || name.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? Path.GetFullPath(name)
            : Locate(name);
        if (directory is null || !System.IO.Directory.Exists(directory))
        {
            throw new FirefoxException(directory is null
                ? $"Firefox has no profile named '{name}' on this machine: none of its profiles.ini lists it."
                : $"The profile folder '{directory}' does not exist.");
        }

        if (LockedBy(directory) is { } holder)
            throw new FirefoxException($"The profile '{name}' is open in another Firefox ({holder}): close it, or use another profile.");

        return new FirefoxProfile(name, directory, disposable: false);
    }

    /// <summary>
    /// Removes the disposable profile folders under <paramref name="root"/> that no running Firefox holds
    /// and that are older than <paramref name="minimumAge"/>: the folders of a host that was killed with
    /// its browsers. A folder just created, whose browser has not locked it yet, is left alone, as is
    /// anything that is not a disposable profile of the tool: a folder named like one but without the
    /// <c>user.js</c> <see cref="Create"/> writes is not ours. Best effort.
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
                if (!IsDisposableName(Path.GetFileName(directory))
                    || !File.Exists(Path.Combine(directory, "user.js"))
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

    /// <summary>Whether a folder name is one <see cref="Create"/> gives: a GUID without dashes.</summary>
    private static bool IsDisposableName(string name) => name.Length == 32 && name.All(Uri.IsHexDigit);

    /// <summary>Removes a disposable profile folder once the browser has ended; a named one is left as it is. Best effort.</summary>
    [SuppressMessage("Design", "CA1031", Justification = "Best-effort cleanup: a profile left behind is only the files of a closed browser.")]
    public async Task RemoveAsync()
    {
        if (!Disposable)
            return;

        // OUT-OF-SCOPE: host folder of the browser process, not a VFS path. A browser that has just
        // ended may still hold a file for an instant: a few tries, then the folder is left behind.
        for (var attempt = 1; attempt <= 3; attempt++)
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

    private static string? Matches(string? sectionName, string? path, bool relative, string iniFolder, string name)
    {
        if (sectionName is null || path is null || !FirefoxProfileChoice.Is(sectionName, name))
            return null;
        return relative ? Path.GetFullPath(Path.Combine(iniFolder, path)) : Path.GetFullPath(path);
    }

    private static string? Locate(string name)
    {
        // OUT-OF-SCOPE: reads the profiles.ini of the host's Firefox.
        var candidates = ProfilesIniCandidates(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            OperatingSystem.IsWindows(), OperatingSystem.IsMacOS());
        foreach (var ini in candidates.Where(File.Exists))
        {
            if (FindInProfilesIni(File.ReadAllText(ini), Path.GetDirectoryName(ini)!, name) is { } directory)
                return directory;
        }

        return null;
    }

    /// <summary>
    /// What holds the profile open, or <c>null</c>: on Unix the <c>lock</c> link names the process, and a
    /// stale link of a crashed Firefox does not count; on Windows <c>parent.lock</c> is held open.
    /// </summary>
    [SuppressMessage("Design", "CA1031", Justification = "A lock file that cannot be read is treated as free: Firefox decides at start.")]
    private static string? LockedBy(string directory)
    {
        // OUT-OF-SCOPE: the lock files of the host's Firefox profile.
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var parentLock = Path.Combine(directory, "parent.lock");
                if (!File.Exists(parentLock))
                    return null;
                using var stream = new FileStream(parentLock, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                return null;
            }

            var link = Path.Combine(directory, "lock");
            var target = new FileInfo(link).LinkTarget;
            if (LockHolder(target) is not { } pid)
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
