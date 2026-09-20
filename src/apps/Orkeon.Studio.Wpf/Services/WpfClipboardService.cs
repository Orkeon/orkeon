using System.Windows;
using Orkeon.Studio.Wpf.ViewModels.Services;

namespace Orkeon.Studio.Wpf.Services;

/// <summary>The real clipboard, for the desktop composition root.</summary>
public sealed class WpfClipboardService : IClipboardService
{
    /// <summary>The one instance the app wires.</summary>
    public static WpfClipboardService Instance { get; } = new();

    /// <inheritdoc />
    public void SetText(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // The clipboard is owned by another process for a moment: a copy that did not land
            // is a copy the user repeats, not a crash of the settings screen.
        }
    }
}
