namespace Orkeon.Studio.Wpf.ViewModels.Mounts;

/// <summary>
/// The last segment of a folder's path, the name a novice reads in place of the whole path
/// (UX lot 02). Both separators count, so a Windows path reads the same on any host.
/// </summary>
public static class FolderNames
{
    /// <summary>« C:\Users\me\Docs\ » → « Docs »; empty for an empty path.</summary>
    public static string Of(string? physicalPath)
    {
        if (string.IsNullOrWhiteSpace(physicalPath))
            return "";

        var trimmed = physicalPath.TrimEnd('\\', '/');
        if (trimmed.Length == 0)
            return physicalPath;

        var cut = trimmed.LastIndexOfAny(['\\', '/']);
        return cut < 0 ? trimmed : trimmed[(cut + 1)..];
    }
}
