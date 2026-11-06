using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Tools.Web.Firefox.BiDi;

/// <summary>
/// The WebSocket a browser started with <c>--remote-debugging-port</c> listens on. Firefox announces it
/// on its standard error: <c>WebDriver BiDi listening on ws://127.0.0.1:41233</c>.
/// </summary>
internal static class BiDiEndpoint
{
    private const string Announcement = "WebDriver BiDi listening on ";

    /// <summary>Reads the endpoint out of one line of the browser's standard error.</summary>
    public static bool TryParse(string? line, [NotNullWhen(true)] out Uri? endpoint)
    {
        endpoint = null;
        if (string.IsNullOrEmpty(line))
            return false;

        var at = line.IndexOf(Announcement, StringComparison.Ordinal);
        if (at < 0)
            return false;

        var address = line[(at + Announcement.Length)..].Trim();
        var end = address.IndexOfAny([' ', '\t']);
        if (end >= 0)
            address = address[..end];

        if (Uri.TryCreate(address, UriKind.Absolute, out var parsed)
            && (parsed.Scheme == Uri.UriSchemeWs || parsed.Scheme == Uri.UriSchemeWss))
        {
            endpoint = parsed;
            return true;
        }

        return false;
    }

    /// <summary>The address a client opens to create a session: <c>/session</c> under the endpoint.</summary>
    public static Uri Session(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return new UriBuilder(endpoint) { Path = "/session" }.Uri;
    }
}
