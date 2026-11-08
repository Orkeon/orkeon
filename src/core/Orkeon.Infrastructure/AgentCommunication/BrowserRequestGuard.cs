namespace Orkeon.Infrastructure.AgentCommunication;

/// <summary>
/// The checks an HTTP listener applies when no request header carries a credential (no <c>ApiKey</c>, no <c>Bearer</c>):
/// a web page the operator opens must not drive it, neither by a cross-site form (which cannot declare a JSON content
/// type without a preflight) nor through a DNS name rebound to the loopback (a <c>Host</c> the listener does not name).
/// A client certificate does not lift them: a browser presents an installed one on its own, as it sends a cookie
/// (FWK final review H8; PR review HOST-3, HOST-5).
/// </summary>
public static class BrowserRequestGuard
{
    /// <summary>Whether the checks apply: no header credential is required.</summary>
    /// <param name="allowedAuthSchemes">The number of header schemes (<c>ApiKey</c>, <c>Bearer</c>) the listener requires.</param>
    public static bool Applies(int allowedAuthSchemes) => allowedAuthSchemes == 0;

    /// <summary>Whether <paramref name="contentType"/> declares <c>application/json</c> (parameters allowed).</summary>
    /// <param name="contentType">The request's <c>Content-Type</c>.</param>
    public static bool IsJson(string? contentType)
    {
        var type = contentType?.Trim();
        return type is not null
            && type.StartsWith("application/json", StringComparison.OrdinalIgnoreCase)
            && (type.Length == 16 || type[16] is ';' or ' ');
    }

    /// <summary>
    /// Whether <paramref name="hostHeader"/> (the request's <c>Host</c>, with its port) names the listener of
    /// <paramref name="prefix"/>. A wildcard prefix (<c>+</c>, <c>*</c>, <c>0.0.0.0</c>, <c>[::]</c>) accepts any host; a loopback prefix accepts the loopback spellings on
    /// its port, so a page reached through a rebound DNS name is refused.
    /// </summary>
    /// <param name="prefix">The listener's prefix (<c>http://localhost:5190/</c>).</param>
    /// <param name="hostHeader">The request's <c>Host</c>.</param>
    public static bool NamesListener(string prefix, string? hostHeader)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        if (prefix.Contains("://+", StringComparison.Ordinal) || prefix.Contains("://*", StringComparison.Ordinal))
            return true;
        if (!Uri.TryCreate(prefix, UriKind.Absolute, out var listener) || listener.Host is "0.0.0.0" or "[::]")
            return true;
        if (string.IsNullOrWhiteSpace(hostHeader) || !Uri.TryCreate("http://" + hostHeader, UriKind.Absolute, out var given))
            return false;
        if (given.Port != listener.Port && !(given.IsDefaultPort && listener.IsDefaultPort))
            return false;
        if (listener.IsLoopback)
            return given.IsLoopback || string.Equals(given.Host, "localhost", StringComparison.OrdinalIgnoreCase);
        return string.Equals(given.Host, listener.Host, StringComparison.OrdinalIgnoreCase);
    }
}
