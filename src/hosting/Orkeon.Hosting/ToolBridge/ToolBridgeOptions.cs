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

    /// <summary>The tools to bridge, by name, or the lone <c>*</c> for every tool the bridge serves.</summary>
    public Collection<string> Allow { get; } = [];

    /// <summary>The tools never bridged, by name; a name here wins over <see cref="Allow"/>.</summary>
    public Collection<string> Deny { get; } = [];

    /// <summary>How long the connection to the bridge may take before a call fails; a call itself has no limit.</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Whether the caller bridges anything: an address and at least one tool allowed.</summary>
    public bool IsEnabled => !string.IsNullOrWhiteSpace(Address) && Allow.Count > 0;

    /// <summary>The problems of these options, judged at start.</summary>
    /// <returns>One sentence per problem; none when they are sound.</returns>
    public IEnumerable<string> Problems()
    {
        if (!string.IsNullOrWhiteSpace(Address) && ToolBridgeAddress.Problem(Address, $"{SectionName}:Address") is { } address)
            yield return address;
        if (string.IsNullOrWhiteSpace(TokenEnvVar))
            yield return $"{SectionName}:TokenEnvVar names no environment variable.";
        if (ConnectTimeout <= TimeSpan.Zero)
            yield return $"{SectionName}:ConnectTimeout must be positive.";
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

    /// <summary>
    /// The listener's prefix. Loopback by default; any other host requires <c>https</c>.
    /// </summary>
    public string Prefix { get; set; } = OperatingSystem.IsWindows() ? "http://localhost:7410/" : "http://127.0.0.1:7410/";

    /// <summary>The environment variable that holds the token; never the token itself.</summary>
    public string TokenEnvVar { get; set; } = ToolBridgeProtocol.DefaultTokenEnvVar;

    /// <summary>The tools served, by name, or the lone <c>*</c> for every tool of the registry.</summary>
    public Collection<string> Allow { get; } = [];

    /// <summary>The tools never served, by name; a name here wins over <see cref="Allow"/>.</summary>
    public Collection<string> Deny { get; } = [];

    /// <summary>The largest body a request may carry, in bytes; a larger one is refused before it is read.</summary>
    public long MaxRequestBytes { get; set; } = DefaultMaxRequestBytes;

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
        foreach (var problem in ToolBridgeSelection.SyntaxProblems(SectionName, Allow, Deny))
            yield return problem;
    }
}

/// <summary>What makes an address of the bridge acceptable: absolute, http or https, a final slash, https off loopback.</summary>
internal static class ToolBridgeAddress
{
    /// <summary>The problem of <paramref name="value"/>, or null when it is acceptable.</summary>
    /// <param name="value">The address or prefix.</param>
    /// <param name="key">The settings key it was read from, for the message.</param>
    /// <returns>One sentence, or null.</returns>
    public static string? Problem(string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return $"{key} '{value}' is not an absolute http or https address.";
        if (!value.EndsWith('/'))
            return $"{key} '{value}' must end with '/'.";
        if (uri.Scheme != Uri.UriSchemeHttps && !IsLoopback(uri))
            return $"{key} '{value}' names a host other than loopback over http: such an address requires https.";
        return null;
    }

    /// <summary>Whether <paramref name="uri"/> names this machine alone.</summary>
    /// <param name="uri">The address.</param>
    /// <returns>True for <c>localhost</c> and the loopback addresses.</returns>
    public static bool IsLoopback(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return uri.IsLoopback;
    }
}
