namespace Orkeon.Tools.Web.Chromium;

/// <summary>What sets one Chromium browser apart: its names, where it installs and where it keeps the user's own data.</summary>
internal sealed record ChromiumVariant
{
    /// <summary>The value of <c>Engine</c> that names it.</summary>
    public required string Id { get; init; }

    /// <summary>The browser's name, as errors write it.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Program names looked up on the PATH, in order.</summary>
    public required IReadOnlyList<string> Names { get; init; }

    /// <summary>The snap of the browser: where its disposable profiles go when it is installed, its temp folder being private.</summary>
    public string? Snap { get; init; }

    /// <summary>Install paths under Windows, relative to the program folders and the user's local application data.</summary>
    public required IReadOnlyList<string> WindowsPrograms { get; init; }

    /// <summary>Install paths under macOS.</summary>
    public required IReadOnlyList<string> MacPrograms { get; init; }

    /// <summary>Install paths under Linux.</summary>
    public required IReadOnlyList<string> LinuxPrograms { get; init; }

    /// <summary>The user's own data folders, relative to the local application data (Windows).</summary>
    public required IReadOnlyList<string> WindowsData { get; init; }

    /// <summary>The user's own data folders, relative to the home (macOS).</summary>
    public required IReadOnlyList<string> MacData { get; init; }

    /// <summary>The user's own data folders, relative to the home (Linux).</summary>
    public required IReadOnlyList<string> LinuxData { get; init; }

    /// <summary>Google Chrome, or Chromium.</summary>
    public static readonly ChromiumVariant Chrome = new()
    {
        Id = "chrome",
        DisplayName = "Chrome",
        Names = ["google-chrome", "google-chrome-stable", "chromium", "chromium-browser", "chrome"],
        Snap = "chromium",
        WindowsPrograms = ["Google/Chrome/Application/chrome.exe"],
        MacPrograms =
        [
            "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
            "/Applications/Chromium.app/Contents/MacOS/Chromium",
        ],
        LinuxPrograms = ["/opt/google/chrome/chrome", "/usr/bin/google-chrome", "/usr/bin/chromium", "/snap/bin/chromium"],
        WindowsData = ["Google/Chrome/User Data", "Chromium/User Data"],
        MacData = ["Library/Application Support/Google/Chrome", "Library/Application Support/Chromium"],
        LinuxData = [".config/google-chrome", ".config/chromium", "snap/chromium/common/chromium"],
    };

    /// <summary>Microsoft Edge.</summary>
    public static readonly ChromiumVariant Edge = new()
    {
        Id = "edge",
        DisplayName = "Edge",
        Names = ["microsoft-edge", "microsoft-edge-stable", "msedge"],
        WindowsPrograms = ["Microsoft/Edge/Application/msedge.exe"],
        MacPrograms = ["/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge"],
        LinuxPrograms = ["/opt/microsoft/msedge/msedge", "/usr/bin/microsoft-edge"],
        WindowsData = ["Microsoft/Edge/User Data"],
        MacData = ["Library/Application Support/Microsoft Edge"],
        LinuxData = [".config/microsoft-edge"],
    };

    /// <summary>Brave.</summary>
    public static readonly ChromiumVariant Brave = new()
    {
        Id = "brave",
        DisplayName = "Brave",
        Names = ["brave-browser", "brave-browser-stable", "brave"],
        Snap = "brave",
        WindowsPrograms = ["BraveSoftware/Brave-Browser/Application/brave.exe"],
        MacPrograms = ["/Applications/Brave Browser.app/Contents/MacOS/Brave Browser"],
        LinuxPrograms = ["/opt/brave.com/brave/brave", "/usr/bin/brave-browser", "/snap/bin/brave"],
        WindowsData = ["BraveSoftware/Brave-Browser/User Data"],
        MacData = ["Library/Application Support/BraveSoftware/Brave-Browser"],
        LinuxData = [".config/BraveSoftware/Brave-Browser", "snap/brave/common/.config/BraveSoftware/Brave-Browser"],
    };

    /// <summary>Where the browser installs on this platform.</summary>
    public IReadOnlyList<string> KnownPrograms(bool windows, bool macOS, IReadOnlyList<string> windowsRoots)
    {
        ArgumentNullException.ThrowIfNull(windowsRoots);
        if (windows)
            return [.. windowsRoots.Where(root => !string.IsNullOrEmpty(root)).SelectMany(root => WindowsPrograms.Select(program => Path.Combine(root, program)))];
        return macOS ? MacPrograms : LinuxPrograms;
    }

    /// <summary>The user's own data folders on this platform, which the browser never opens to a remote protocol.</summary>
    public IReadOnlyList<string> UserDataFolders(bool windows, bool macOS, string home, string localData)
    {
        var (root, folders) = windows ? (localData, WindowsData) : (home, macOS ? MacData : LinuxData);
        return string.IsNullOrEmpty(root) ? [] : [.. folders.Select(folder => Path.Combine([root, .. folder.Split('/')]))];
    }
}
