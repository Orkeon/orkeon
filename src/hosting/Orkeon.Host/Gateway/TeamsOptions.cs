using Orkeon.Constants.Configuration;

namespace Orkeon.Host.Gateway;

/// <summary>One room of Microsoft Teams and the hosted crew it reaches.</summary>
internal sealed record TeamsRouteOptions
{
    /// <summary>The Teams channel (or chat) id, as Teams writes it: <c>19:…@thread.tacv2</c>.</summary>
    public string Channel { get; init; } = string.Empty;

    /// <summary>The hosted crew a thread opened in that channel starts.</summary>
    public string Crew { get; init; } = string.Empty;
}

/// <summary>
/// The Microsoft Teams channel's configuration (#446).
/// <para>
/// **The secret is named, never written.** <see cref="SecretEnvironmentVariable"/> holds the *name* of an environment
/// variable, as the Discord channel's token does.
/// </para>
/// </summary>
internal sealed record TeamsChannelOptions
{
    /// <summary>Configuration section this binds to.</summary>
    public const string SectionName = "Orkeon:Host:Teams";

    /// <summary>The path the bot registration's messaging endpoint ends with.</summary>
    public const string MessagesPath = "/api/messages";

    /// <summary>The default listener prefix: the loopback, on the port bots conventionally use.</summary>
    public const string DefaultPrefix = "http://localhost:3978/";

    /// <summary>The hosts Microsoft publishes for the Teams connector of the public cloud and of GCC.</summary>
    internal static readonly string[] KnownServiceHosts = ["smba.trafficmanager.net", "smba.infra.gcc.teams.microsoft.com"];

    /// <summary>Whether the channel is switched on. Off unless a deployment says otherwise.</summary>
    public bool Enabled { get; init; }

    /// <summary>The bot's application (client) id: the audience every incoming token must carry.</summary>
    public string AppId { get; init; } = string.Empty;

    /// <summary>The Entra tenant the bot is registered in (single-tenant). An activity of another tenant is refused.</summary>
    public string TenantId { get; init; } = string.Empty;

    /// <summary>Name of the environment variable holding the application's client secret.</summary>
    public string SecretEnvironmentVariable { get; init; } = EnvironmentVariableNames.TeamsAppSecret;

    /// <summary>
    /// Entra object ids of the people allowed to talk to the bot. **Empty denies everyone**, and an enabled channel refuses
    /// the start.
    /// </summary>
    public IReadOnlyList<string> AllowedUserIds { get; init; } = [];

    /// <summary>
    /// The listener's prefix: scheme, host, port and a closing slash. The channel answers <c>POST /api/messages</c> there;
    /// Microsoft must reach it over HTTPS, through a reverse proxy that terminates TLS in front of it.
    /// </summary>
    public string Prefix { get; init; } = DefaultPrefix;

    /// <summary>
    /// Which crew each room reaches. A list, not a map like Discord's: a Teams channel id contains <c>:</c>, which a
    /// configuration key cannot hold.
    /// </summary>
    public IReadOnlyList<TeamsRouteOptions> Routes { get; init; } = [];

    /// <summary>
    /// The crew a room without a route reaches. Unset, it is the first crew declared under
    /// <c>Orkeon:Host:Crews</c>; set, it must name one of them.
    /// </summary>
    public string? DefaultCrew { get; init; }

    /// <summary>How long progress updates are spaced out. Teams limits what a bot posts in one conversation.</summary>
    public TimeSpan ProgressInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Hosts of the Bot Connector service replies may go to, beside the ones Microsoft publishes for Teams
    /// (<c>smba.trafficmanager.net</c>, <c>smba.infra.gcc.teams.microsoft.com</c>). An activity naming another host is
    /// refused, whatever its token says.
    /// </summary>
    public IReadOnlyList<string> ServiceHosts { get; init; } = [];

    /// <summary>Reads the secret from the environment, or null when the variable is unset.</summary>
    public string? ReadSecret() =>
        Environment.GetEnvironmentVariable(SecretEnvironmentVariable) is { Length: > 0 } secret ? secret : null;

    /// <summary>Refuses what cannot work, each refusal a sentence naming the key to fix.</summary>
    /// <exception cref="HostConfigurationException">The configuration is refused.</exception>
    public void Validate()
    {
        if (AllowedUserIds.Count == 0)
            throw new HostConfigurationException(
                "The Teams channel is enabled but Teams:AllowedUserIds is empty — it would answer nobody.");

        foreach (var id in AllowedUserIds.Where(id => !Guid.TryParseExact(id, "D", out _)))
            throw new HostConfigurationException(
                $"Teams:AllowedUserIds contains '{id}', which is not an Entra object id (a GUID).");

        if (!Guid.TryParseExact(AppId, "D", out _))
            throw new HostConfigurationException($"Teams:AppId is '{AppId}': write the bot's application (client) id, a GUID.");

        if (!Guid.TryParseExact(TenantId, "D", out _))
            throw new HostConfigurationException($"Teams:TenantId is '{TenantId}': write the Entra tenant id, a GUID.");

        if (ReadSecret() is null)
            throw new HostConfigurationException(
                $"The Teams channel is enabled but the environment variable '{SecretEnvironmentVariable}' is empty or unset.");

        if (ProgressInterval <= TimeSpan.Zero)
            throw new HostConfigurationException($"Teams:ProgressInterval must be positive; got {ProgressInterval}.");

        foreach (var host in ServiceHosts.Where(host => Uri.CheckHostName(host) != UriHostNameType.Dns))
            throw new HostConfigurationException($"Teams:ServiceHosts contains '{host}', which is not a host name.");

        ValidatePrefix();
    }

    private void ValidatePrefix()
    {
        var parseable = Prefix.Replace("://+", "://any.invalid", StringComparison.Ordinal)
            .Replace("://*", "://any.invalid", StringComparison.Ordinal);
        if (!Prefix.EndsWith('/')
            || !Uri.TryCreate(parseable, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || uri.PathAndQuery != "/"
            || uri.Fragment.Length > 0
            || uri.UserInfo.Length > 0)
        {
            throw new HostConfigurationException(
                $"Teams:Prefix is '{Prefix}': write a listener prefix such as '{DefaultPrefix}' "
                + "(http or https, a host, a port, a closing slash, no path).");
        }

        if (uri.Scheme == Uri.UriSchemeHttps && !OperatingSystem.IsWindows())
            throw new HostConfigurationException(
                $"Teams:Prefix is '{Prefix}': the listener serves https on Windows only. "
                + "Listen on http behind a reverse proxy that terminates TLS.");
    }

    /// <summary>Whether the prefix is plain http beyond the loopback, where the bearer tokens cross the network in clear.</summary>
    internal bool ListensInClearBeyondLoopback =>
        Prefix.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        && !(Uri.TryCreate(Prefix, UriKind.Absolute, out var uri) && uri.IsLoopback);
}
