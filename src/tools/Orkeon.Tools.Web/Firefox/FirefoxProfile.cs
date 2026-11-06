using System.Diagnostics.CodeAnalysis;
using Orkeon.Compliance.Vfs;

namespace Orkeon.Tools.Web.Firefox;

/// <summary>
/// The profile folder of one browser: created empty with the preferences below, removed when the browser
/// closes. A browser never shares the user's own profile, and two browsers never share one.
/// </summary>
internal sealed class FirefoxProfile
{
    /// <summary>The folder a Firefox snap may write under the user's home: its temp folder is private.</summary>
    internal const string SnapCommon = "snap/firefox/common";

    // remote.active-protocols 1: WebDriver BiDi alone, no CDP. The others keep a fresh profile quiet:
    // no first-run pages, no default-browser question, no telemetry, no update, no saved password.
    private const string Preferences = """
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

    private FirefoxProfile(string directory) => Directory = directory;

    /// <summary>Physical path of the profile folder, handed to Firefox as <c>--profile</c>.</summary>
    public string Directory { get; }

    /// <summary>
    /// Where profiles go when the options name no folder: under the snap's own folder when Firefox is a
    /// snap, else under the system's temp folder.
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
    [SuppressVfsCompliance("OUT-OF-SCOPE: probes the host for the Firefox snap's folder, to place the browser's own profile where the snap can read it; not a VFS path.")]
    public static string ResolveRoot(string? configured)
    {
        // OUT-OF-SCOPE: host probe of the browser's install kind, not a VFS mount.
        return string.IsNullOrWhiteSpace(configured)
            ? DefaultRoot(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Path.GetTempPath(), System.IO.Directory.Exists)
            : Path.GetFullPath(configured);
    }

    /// <summary>Creates a new profile folder under <paramref name="root"/>.</summary>
    /// <exception cref="FirefoxException">The folder could not be created.</exception>
    [SuppressVfsCompliance("OUT-OF-SCOPE: the browser's own profile folder on the host, outside every mount; Firefox, not the framework, reads and writes there.")]
    public static FirefoxProfile Create(string root)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);

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

        return new FirefoxProfile(directory);
    }

    /// <summary>Removes the profile folder; called once the browser has ended. Best effort.</summary>
    [SuppressVfsCompliance("OUT-OF-SCOPE: removes the browser's own profile folder on the host, outside every mount.")]
    [SuppressMessage("Design", "CA1031", Justification = "Best-effort cleanup: a profile left behind is only the files of a closed browser.")]
    public async Task RemoveAsync()
    {
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
}
