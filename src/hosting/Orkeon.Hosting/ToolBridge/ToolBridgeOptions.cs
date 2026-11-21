using System.Collections.ObjectModel;

namespace Orkeon.Hosting.ToolBridge;

/// <summary>
/// The caller's side of the tool bridge, section <c>Orkeon:Tools:Bridge</c>: the tools of this Orkeon that run on
/// another machine. Nothing is bridged while <c>Address</c> is unset or <c>Allow</c> is empty.
/// </summary>
internal sealed class ToolBridgeClientOptions
{
    /// <summary>The section the options are bound from.</summary>
    public const string SectionName = "Orkeon:Tools:Bridge";

    /// <summary>The bridge's base address (<c>https://host:7410/</c>); unset, nothing is bridged.</summary>
    public string? Address { get; set; }

    /// <summary>The environment variable that holds the token; never the token itself.</summary>
    public string TokenEnvVar { get; set; } = ToolBridgeProtocol.DefaultTokenEnvVar;

    /// <summary>The tools to bridge, by name, or the lone <c>*</c> for every bridgeable Orkeon tool the bridge serves.</summary>
    public Collection<string> Allow { get; } = [];

    /// <summary>The tools never bridged, by name; a name here wins over <see cref="Allow"/>.</summary>
    public Collection<string> Deny { get; } = [];

    /// <summary>How long the connection to the bridge may take before a call fails; a call itself has no limit.</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The largest total, in bytes, of the files of one call: those a call sends to the bridge, and those it brings back.
    /// </summary>
    public long MaxFileBytes { get; set; } = ToolBridgeServerOptions.DefaultMaxFileBytes;

    /// <summary>
    /// The largest answer, in bytes, this caller reads from the bridge; a larger one fails the call with its reason. The
    /// default leaves room for <see cref="MaxFileBytes"/> of files in base64 beside the result.
    /// </summary>
    public long MaxResponseBytes { get; set; } = DefaultMaxResponseBytes;

    /// <summary>
    /// The <c>Host</c> header sent to the bridge, when it differs from the address's: a container of Docker Desktop
    /// reaches the bridge's loopback listener through <c>host.docker.internal</c>, while the listener answers only the
    /// host its prefix names. Unset, it is <c>127.0.0.1:&lt;port&gt;</c> for an address naming
    /// <c>host.docker.internal</c>, and the address's own otherwise.
    /// </summary>
    public string? HostHeader { get; set; }

    /// <summary>The default largest answer read from the bridge: 32 MiB.</summary>
    public const long DefaultMaxResponseBytes = 32L * 1024 * 1024;

    /// <summary>The <c>Host</c> header the client sends: <see cref="HostHeader"/>, or the default for Docker Desktop.</summary>
    /// <returns>The header, or null for the address's own.</returns>
    public string? EffectiveHostHeader()
    {
        if (!string.IsNullOrWhiteSpace(HostHeader))
            return HostHeader.Trim();
        return Uri.TryCreate(Address, UriKind.Absolute, out var uri) && ToolBridgeAddress.IsDockerHost(uri)
            ? "127.0.0.1:" + uri.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : null;
    }

    /// <summary>Whether the address reaches the bridge over http through Docker Desktop's name for the host.</summary>
    public bool IsDockerHostOverHttp =>
        Uri.TryCreate(Address, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttp && ToolBridgeAddress.IsDockerHost(uri);

    /// <summary>Whether the caller bridges anything: an address and at least one tool allowed.</summary>
    public bool IsEnabled => !string.IsNullOrWhiteSpace(Address) && Allow.Count > 0;

    /// <summary>The problems of these options, judged at start.</summary>
    /// <returns>One sentence per problem; none when they are sound.</returns>
    public IEnumerable<string> Problems()
    {
        if (!string.IsNullOrWhiteSpace(Address) && ToolBridgeAddress.Problem(Address, $"{SectionName}:Address", allowDockerHost: true) is { } address)
            yield return address;
        if (!string.IsNullOrWhiteSpace(HostHeader) && !ToolBridgeAddress.IsAuthority(HostHeader.Trim()))
            yield return $"{SectionName}:HostHeader '{HostHeader}' is not a host with an optional port (127.0.0.1:7410).";
        if (string.IsNullOrWhiteSpace(TokenEnvVar))
            yield return $"{SectionName}:TokenEnvVar names no environment variable.";
        if (ConnectTimeout <= TimeSpan.Zero)
            yield return $"{SectionName}:ConnectTimeout must be positive.";
        if (MaxFileBytes <= 0)
            yield return $"{SectionName}:MaxFileBytes must be positive.";
        if (MaxResponseBytes <= 0 || MaxResponseBytes > int.MaxValue)
            yield return $"{SectionName}:MaxResponseBytes must be positive and at most {int.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture)}.";
        foreach (var problem in ToolBridgeSelection.SyntaxProblems(SectionName, Allow, Deny))
            yield return problem;
    }
}

/// <summary>
/// The serving side of the tool bridge, section <c>Orkeon:Bridge</c>: the tools <c>orkeon bridge serve</c> runs for a
/// caller on another machine. Nothing is served while <c>Allow</c> is empty.
/// </summary>
internal sealed class ToolBridgeServerOptions
{
    /// <summary>The section the options are bound from.</summary>
    public const string SectionName = "Orkeon:Bridge";

    /// <summary>The default largest body of a request: 32 MiB.</summary>
    public const long DefaultMaxRequestBytes = 32L * 1024 * 1024;

    /// <summary>The default largest total of the files of one call, each way: 16 MiB.</summary>
    public const long DefaultMaxFileBytes = 16L * 1024 * 1024;

    /// <summary>
    /// The listener's prefix. Loopback by default, <c>localhost</c> under Windows, whose URL reservations name it,
    /// <c>127.0.0.1</c> elsewhere; any other host requires <c>https</c>.
    /// </summary>
    public string Prefix { get; set; } = OperatingSystem.IsWindows() ? "http://localhost:7410/" : "http://127.0.0.1:7410/";

    /// <summary>The environment variable that holds the token; never the token itself.</summary>
    public string TokenEnvVar { get; set; } = ToolBridgeProtocol.DefaultTokenEnvVar;

    /// <summary>The tools served, by name, or the lone <c>*</c> for every bridgeable Orkeon tool of the registry.</summary>
    public Collection<string> Allow { get; } = [];

    /// <summary>The tools never served, by name; a name here wins over <see cref="Allow"/>.</summary>
    public Collection<string> Deny { get; } = [];

    /// <summary>The largest body a request may carry, in bytes; a larger one is refused before it is read.</summary>
    public long MaxRequestBytes { get; set; } = DefaultMaxRequestBytes;

    /// <summary>
    /// The largest total, in bytes, of the files of one call: those a caller sends, and those the tool wrote that go
    /// back.
    /// </summary>
    public long MaxFileBytes { get; set; } = DefaultMaxFileBytes;

    /// <summary>
    /// How long the bridge waits for a sign of a caller that has runs on it (a heartbeat or a request) before it ends
    /// those runs, and their browsers, at once.
    /// </summary>
    public TimeSpan HeartbeatWindow { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>The shortest heartbeat window either side accepts.</summary>
    public static readonly TimeSpan MinHeartbeatWindow = TimeSpan.FromSeconds(1);

    /// <summary>The longest heartbeat window either side accepts.</summary>
    public static readonly TimeSpan MaxHeartbeatWindow = TimeSpan.FromMinutes(10);

    /// <summary>The problems of these options, judged at start.</summary>
    /// <returns>One sentence per problem; none when they are sound.</returns>
    public IEnumerable<string> Problems()
    {
        if (ToolBridgeAddress.Problem(Prefix, $"{SectionName}:Prefix") is { } prefix)
            yield return prefix;
        if (string.IsNullOrWhiteSpace(TokenEnvVar))
            yield return $"{SectionName}:TokenEnvVar names no environment variable.";
        if (MaxRequestBytes <= 0)
            yield return $"{SectionName}:MaxRequestBytes must be positive.";
        if (MaxFileBytes <= 0)
            yield return $"{SectionName}:MaxFileBytes must be positive.";
        if (HeartbeatWindow < MinHeartbeatWindow || HeartbeatWindow > MaxHeartbeatWindow)
            yield return $"{SectionName}:HeartbeatWindow must lie between {MinHeartbeatWindow} and {MaxHeartbeatWindow}.";
        foreach (var problem in ToolBridgeSelection.SyntaxProblems(SectionName, Allow, Deny))
            yield return problem;
    }
}

/// <summary>
/// What makes an address of the bridge acceptable: absolute, http or https, a final slash, https off loopback; the
/// caller's address may also name Docker Desktop's <c>host.docker.internal</c> over http.
/// </summary>
internal static class ToolBridgeAddress
{
    /// <summary>The name Docker Desktop gives the host machine inside a container.</summary>
    public const string DockerHost = "host.docker.internal";

    /// <summary>The problem of <paramref name="value"/>, or null when it is acceptable.</summary>
    /// <param name="value">The address or prefix.</param>
    /// <param name="key">The settings key it was read from, for the message.</param>
    /// <param name="allowDockerHost">Whether http may name <see cref="DockerHost"/> (the caller's address only).</param>
    /// <returns>One sentence, or null.</returns>
    public static string? Problem(string? value, string key, bool allowDockerHost = false)
    {
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return $"{key} '{value}' is not an absolute http or https address.";
        if (uri.UserInfo.Length > 0)
            return $"{key} names a user and a password before its host: the token travels in its environment variable, never in the address.";
        if (!value.EndsWith('/'))
            return $"{key} '{value}' must end with '/'.";
        if (uri.Scheme != Uri.UriSchemeHttps && !IsLoopback(uri) && !(allowDockerHost && IsDockerHost(uri)))
            return $"{key} '{value}' names a host other than loopback over http: such an address requires https.";
        return null;
    }

    /// <summary>
    /// Whether <paramref name="uri"/> names this machine alone: a loopback IP literal, or the name <c>localhost</c>.
    /// <see cref="Uri.IsLoopback"/> also takes the name <c>loopback</c>, which a resolver may answer with any address,
    /// so it is not relied on.
    /// </summary>
    /// <param name="uri">The address.</param>
    /// <returns>True for <c>localhost</c> and the loopback addresses.</returns>
    public static bool IsLoopback(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6)
            return System.Net.IPAddress.TryParse(uri.IdnHost, out var address) && System.Net.IPAddress.IsLoopback(address);
        // Uri reports the name loopback as localhost: the host is read as written.
        return string.Equals(HostAsWritten(uri), "localhost", StringComparison.OrdinalIgnoreCase);
    }

    private static string HostAsWritten(Uri uri)
    {
        var text = uri.OriginalString;
        var start = text.IndexOf("://", StringComparison.Ordinal);
        if (start < 0)
            return uri.IdnHost;
        start += 3;
        var authorityEnd = text.IndexOfAny(['/', '?', '#'], start);
        var at = text.IndexOf('@', start);
        if (at >= 0 && (authorityEnd < 0 || at < authorityEnd))
            start = at + 1;
        var end = text.IndexOfAny([':', '/', '?', '#'], start);
        return end < 0 ? text[start..] : text[start..end];
    }

    /// <summary>Whether <paramref name="uri"/> names <see cref="DockerHost"/>.</summary>
    /// <param name="uri">The address.</param>
    /// <returns>True for Docker Desktop's name of the host, in any case.</returns>
    public static bool IsDockerHost(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return string.Equals(uri.IdnHost, DockerHost, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether <paramref name="value"/> is a host with an optional port, and nothing else.</summary>
    /// <param name="value">The value of a <c>Host</c> header.</param>
    /// <returns>True for <c>127.0.0.1:7410</c>, <c>localhost</c>, <c>[::1]:7410</c>.</returns>
    public static bool IsAuthority(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length == 0 || value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is '/' or '@' or '?' or '#' or '\\'))
            return false;
        return Uri.TryCreate("http://" + value + "/", UriKind.Absolute, out var uri)
            && uri.UserInfo.Length == 0 && uri.PathAndQuery == "/" && uri.Host.Length > 0;
    }
}
