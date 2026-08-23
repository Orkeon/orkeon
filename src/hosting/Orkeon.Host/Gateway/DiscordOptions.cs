namespace Orkeon.Host.Gateway;

/// <summary>
/// The Discord channel's configuration.
/// <para>
/// **The token is named, never written.** <see cref="TokenEnvironmentVariable"/> holds the
/// *name* of an environment variable; the value never touches the configuration file, a commit
/// or a container image layer. The LLM providers already follow this rule, and a bot token —
/// which can read every message a server sends — does not get an exception.
/// </para>
/// </summary>
internal sealed record DiscordChannelOptions
{
    /// <summary>Configuration section this binds to.</summary>
    public const string SectionName = "Orkeon:Host:Discord";

    /// <summary>Whether the channel is switched on. Off unless a deployment says otherwise.</summary>
    public bool Enabled { get; init; }

    /// <summary>Name of the environment variable holding the bot token.</summary>
    public string TokenEnvironmentVariable { get; init; } = "ORKEON_DISCORD_TOKEN";

    /// <summary>
    /// Discord user ids allowed to talk to the bot. **Empty denies everyone** — a bot on a
    /// public server with an open door spends someone's API budget on strangers.
    /// </summary>
    public IReadOnlyList<string> AllowedUserIds { get; init; } = [];

    /// <summary>
    /// How long progress updates are spaced out. Discord's rate limit is per channel and
    /// unforgiving; a message per agent thought would exhaust it inside one crew.
    /// </summary>
    public TimeSpan ProgressInterval { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Guild (server) ids to register the slash commands in. **Empty registers them
    /// globally**, which needs no configuration but is cached by Discord for up to an hour;
    /// naming guilds makes `/status` and `/stop` available immediately — the dev loop.
    /// </summary>
    public IReadOnlyList<string> GuildIds { get; init; } = [];

    /// <summary>
    /// Reads the token from the environment, or null when the variable is unset. Returning
    /// null rather than throwing lets the host say *which* variable is missing, which is the
    /// only actionable form of that error.
    /// </summary>
    public string? ReadToken() =>
        Environment.GetEnvironmentVariable(TokenEnvironmentVariable) is { Length: > 0 } token
            ? token
            : null;
}
