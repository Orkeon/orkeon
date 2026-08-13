using Microsoft.Win32;
using Orkeon.Compliance.Vfs;
using Orkeon.Studio.Wpf.ViewModels.Services;

namespace Orkeon.Studio.Wpf.Services;

/// <summary>
/// The WPF implementation of <see cref="IPathPicker"/>, over the common dialogs shipped with .NET.
/// It is deliberately the only place in the app that knows a dialog type exists.
/// </summary>
[SuppressVfsCompliance(
    "EXCEPTION-BOOTSTRAP: Studio is a host application. The browse dialogs need an existing directory "
    + "to open at, on the physical disk, before any VFS mount exists — the path being picked is often "
    + "the one about to become a mount root.")]
public sealed class WindowPathPicker : IPathPicker
{
    /// <inheritdoc />
    public string? PickFolder(string title, string? initialPath = null)
    {
        var dialog = new OpenFolderDialog { Title = title, Multiselect = false };
        ApplyInitialDirectory(dialog, initialPath);

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    /// <inheritdoc />
    public string? PickFile(string title, string filter, string? initialPath = null)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
        ApplyInitialDirectory(dialog, initialPath);

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    /// <inheritdoc />
    public string? PickSaveFile(string title, string filter, string? suggestedPath = null)
    {
        var dialog = new SaveFileDialog { Title = title, Filter = filter, OverwritePrompt = true };
        ApplyInitialDirectory(dialog, suggestedPath);

        if (suggestedPath is { Length: > 0 })
            dialog.FileName = System.IO.Path.GetFileName(suggestedPath);

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private static void ApplyInitialDirectory(CommonItemDialog dialog, string? path)
    {
        if (path is not { Length: > 0 })
            return;

        // The path may not exist yet — it is a suggestion, so a missing directory is not an error.
        var directory = System.IO.Directory.Exists(path) ? path : System.IO.Path.GetDirectoryName(path);
        if (directory is { Length: > 0 } && System.IO.Directory.Exists(directory))
            dialog.InitialDirectory = directory;
    }
}
