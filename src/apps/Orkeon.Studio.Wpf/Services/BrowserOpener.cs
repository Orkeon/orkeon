using System.Diagnostics;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.Services;

/// <summary>
/// The real browser opener (STUDIO-70): the default browser, through the shell — and through it
/// only for an absolute <c>https</c> address. The check is made again here, on the very string
/// handed to the shell: whatever a caller passes, a path, a <c>file:</c> address or another
/// scheme never reaches <c>UseShellExecute</c> from this type.
/// </summary>
public sealed class BrowserOpener : IBrowserOpener
{
    /// <summary>Shared instance.</summary>
    public static readonly BrowserOpener Instance = new();

    /// <inheritdoc />
    public void Open(Uri address)
    {
        // AbsoluteUri, never OriginalString: what is opened is what was checked, in its canonical spelling.
        if (address is null || !address.IsAbsoluteUri)
            return;

        if (!HttpsAddress.TryParse(address.AbsoluteUri, out var checkedAddress))
            return;

        try
        {
            Process.Start(new ProcessStartInfo(checkedAddress.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception
            or InvalidOperationException or System.IO.FileNotFoundException)
        {
            // No browser registered, or the shell refused: the address is still on screen to copy.
        }
    }
}
