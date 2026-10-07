using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Studio.Wpf.ViewModels.Mvvm;

/// <summary>
/// Opens a web address in the person's browser (STUDIO-70). A port of its own, beside
/// <see cref="IShellOpener"/>: that one hands a path to the shell, which runs whatever the path
/// names; this one is only ever given an <c>https</c> address, checked by
/// <see cref="HttpsAddress"/> on both sides of the port — the view model that asks, and the
/// implementation that opens. Studio never calls it on its own: only a click does.
/// </summary>
public interface IBrowserOpener
{
    /// <summary>
    /// Opens <paramref name="address"/> in the default browser; ignores anything that is not an
    /// absolute <c>https</c> address, and silently ignores a failure to open.
    /// </summary>
    void Open(Uri address);
}

/// <summary>
/// A browser opener that opens nothing, on the model of <see cref="NullShellOpener"/>: the sign-in
/// panel offers "Open in the browser" only when it has a port, so the screenshot campaign keeps
/// the button on screen with this one, and no browser ever starts under it.
/// </summary>
public sealed class NullBrowserOpener : IBrowserOpener
{
    /// <summary>The shared instance; the type is stateless.</summary>
    public static NullBrowserOpener Instance { get; } = new();

    /// <inheritdoc />
    public void Open(Uri address)
    {
        // Deliberately inert: see the type's summary.
    }
}

/// <summary>
/// The one rule on an address Studio is asked to open: absolute, and <c>https</c>. The address of
/// a sign-in comes from the CLI's output, and what the shell does with a string depends on what
/// the string is — a path runs a program, another scheme starts whatever handles it.
/// </summary>
public static class HttpsAddress
{
    /// <summary>Reads <paramref name="text"/> as an absolute <c>https</c> address; false for anything else.</summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out Uri? address)
    {
        if (Uri.TryCreate(text?.Trim(), UriKind.Absolute, out var parsed) && Is(parsed))
        {
            address = parsed;
            return true;
        }

        address = null;
        return false;
    }

    /// <summary>
    /// Whether <paramref name="address"/> is an absolute <c>https</c> address with a host: never
    /// a path, a UNC share, a <c>file:</c> address or any other scheme.
    /// </summary>
    public static bool Is([NotNullWhen(true)] Uri? address) =>
        address is { IsAbsoluteUri: true, IsFile: false, IsUnc: false, Host.Length: > 0 }
        && string.Equals(address.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
}
