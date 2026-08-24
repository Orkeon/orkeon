using System.Diagnostics;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.Services;

/// <summary>The real shell opener: Explorer via UseShellExecute.</summary>
public sealed class ShellOpener : IShellOpener
{
    /// <summary>Shared instance.</summary>
    public static readonly ShellOpener Instance = new();

    /// <inheritdoc />
    public void Open(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception
            or InvalidOperationException or System.IO.FileNotFoundException)
        {
            // A missing folder or a refused shell verb must never crash the window.
        }
    }
}
