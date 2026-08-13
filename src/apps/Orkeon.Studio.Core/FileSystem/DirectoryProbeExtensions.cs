using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Studio.Core.FileSystem;

/// <summary>
/// The "create the folder" remediation both mount forms offer: a mount whose physical path
/// does not exist is refused at boot, and creating it is the fix. Reporting the failure
/// rather than throwing is what lets a form show it next to the field.
/// </summary>
public static class DirectoryProbeExtensions
{
    /// <summary>Creates <paramref name="path"/>, returning the reason instead of throwing.</summary>
    /// <param name="directories">The probe that owns the disk access.</param>
    /// <param name="path">The directory to create; blank is refused.</param>
    /// <param name="error">Why nothing was created; <see langword="null"/> on success.</param>
    public static bool TryCreate(
        this IDirectoryProbe directories,
        string? path,
        [NotNullWhen(false)] out string? error)
    {
        ArgumentNullException.ThrowIfNull(directories);

        error = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            error = "Pick a physical path first.";
            return false;
        }

        try
        {
            directories.Create(path.Trim());
            return true;
        }
        catch (IOException ex)
        {
            error = ex.Message;
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
