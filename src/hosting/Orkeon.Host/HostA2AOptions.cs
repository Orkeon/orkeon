using System.Globalization;
using Orkeon.Infrastructure.Constants.Network;

namespace Orkeon.Host;

/// <summary>
/// The crews other agents may run over A2A (GAP-23), bound from <c>Orkeon:Host:A2A</c>.
/// <para>
/// Off by default, and exposing is a choice per crew — like a chat route. A daemon that listens
/// on a port nobody asked it to open, or offers every crew it hosts to whoever reaches that
/// port, is a surprise its operator learns about from someone else.
/// </para>
/// <para>
/// Only the listener and the exposed crews live here. The card's identity
/// (<c>A2A:AgentName</c>, …) and the credentials a peer must present (<c>A2A:Security</c>) are
/// the A2A section's, read as for any Orkeon A2A server.
/// </para>
/// </summary>
internal sealed record HostA2AOptions
{
    /// <summary>Configuration section this binds to.</summary>
    public const string SectionName = OrkeonHostOptions.SectionName + ":A2A";

    /// <summary>Serves the exposed crews over A2A. Off, the host listens on no port.</summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// The listener's scheme and host name: <c>http://localhost</c> by default, <c>http://+</c>
    /// for every interface. No port (that is <see cref="Port"/>) and no path.
    /// </summary>
    public string Host { get; init; } = "http://localhost";

    /// <summary>The listener's port.</summary>
    public int Port { get; init; } = NetworkDefaults.A2APort;

    /// <summary>
    /// The hosted crews other agents may run, by name — one skill each. None by default.
    /// </summary>
    public IReadOnlyList<string> Crews { get; init; } = [];

    /// <summary>
    /// The declared crews this section exposes, in its order, each once — refusing an entry
    /// that names no declared crew, and a section that exposes none.
    /// </summary>
    /// <param name="declared">The crews <c>Orkeon:Host:Crews</c> declares.</param>
    /// <exception cref="HostConfigurationException">The section cannot be honoured.</exception>
    public IReadOnlyList<HostedCrewOptions> ExposedCrews(IReadOnlyList<HostedCrewOptions> declared)
    {
        ArgumentNullException.ThrowIfNull(declared);

        if (Crews.Count == 0)
        {
            throw new HostConfigurationException(
                $"{SectionName} is enabled but exposes no crew: list under {SectionName}:Crews the crews other "
                + $"agents may run ({Names(declared)}) — exposing is a choice per crew.");
        }

        var exposed = new List<HostedCrewOptions>();
        foreach (var name in Crews)
        {
            // Case-insensitive, like the registry and a chat route; the skill keeps the declared
            // spelling, the one id the card publishes and the router compares.
            var crew = declared.FirstOrDefault(c => string.Equals(c.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new HostConfigurationException(
                    $"{SectionName}:Crews exposes '{name}', which is not a crew declared under "
                    + $"'{OrkeonHostOptions.SectionName}:Crews' ({Names(declared)}).");

            if (!exposed.Contains(crew))
                exposed.Add(crew);
        }

        return exposed;
    }

    /// <summary>
    /// Refuses a <see cref="Host"/> or <see cref="Port"/> the listener cannot take — before it
    /// throws out of the server's start, where a typo would read as a crash and restart in a loop.
    /// </summary>
    /// <exception cref="HostConfigurationException">The listener is malformed.</exception>
    public void ValidateListener()
    {
        if (ListenerHostName(Host) is null)
        {
            throw new HostConfigurationException(
                $"{SectionName}:Host is '{Host}', which is not a listener host: write the scheme and the host "
                + "name alone — http://localhost (the default), http://+ for every interface, or "
                + $"https://<name> — the port is {SectionName}:Port.");
        }

        if (Port is < 1 or > 65535)
        {
            throw new HostConfigurationException(
                $"{SectionName}:Port is {Port.ToString(CultureInfo.InvariantCulture)}; a port is between 1 and 65535.");
        }
    }

    /// <summary>
    /// Whether the listener stays on the loopback interface (<c>localhost</c>, <c>127.0.0.1</c>,
    /// <c>[::1]</c>): only then may it serve without authentication.
    /// </summary>
    public bool ListensOnLoopbackOnly =>
        ListenerHostName(Host) is { } name
        && name is not ("+" or "*")
        && Uri.TryCreate($"http://{name}/", UriKind.Absolute, out var uri)
        && uri.IsLoopback;

    /// <summary>
    /// The host name of an <c>http://</c> or <c>https://</c> listener — a name, an address or a
    /// wildcard (<c>+</c>, <c>*</c>) — or null when <paramref name="host"/> is not one: no
    /// scheme, a port, a path, or nothing at all.
    /// </summary>
    private static string? ListenerHostName(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return null;

        var trimmed = host.TrimEnd('/');
        string name;
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            name = trimmed["http://".Length..];
        else if (trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            name = trimmed["https://".Length..];
        else
            return null;

        if (name is "+" or "*")
            return name;

        // An IPv6 address keeps its colons inside brackets; any other colon is a port.
        var bare = name.StartsWith('[') && name.EndsWith(']') ? name[1..^1] : name;
        if (bare.Length == 0 || (bare.Contains(':', StringComparison.Ordinal) && bare.Length == name.Length))
            return null;

        return Uri.CheckHostName(bare) == UriHostNameType.Unknown ? null : name;
    }

    private static string Names(IReadOnlyList<HostedCrewOptions> crews) =>
        crews.Count == 0 ? "none is declared" : string.Join(", ", crews.Select(c => $"'{c.Name}'"));
}
