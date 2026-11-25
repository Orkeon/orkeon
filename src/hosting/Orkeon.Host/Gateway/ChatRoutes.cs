using System.Globalization;

namespace Orkeon.Host.Gateway;

/// <summary>
/// Which hosted crew a chat room reaches (GAP-11).
/// <para>
/// A room is where a conversation is opened — on Discord, the channel a thread belongs to.
/// <c>Orkeon:Host:Discord:Routes</c> (or <c>Orkeon:Host:Teams:Routes</c>) maps a room to a crew; every other room reaches the
/// default crew — <c>DefaultCrew</c>, or the first declared. Until this, every message went
/// to the first crew: the others were hosted, bounded, and unreachable. One room, one crew is
/// the mapping a person can predict without being told: <c>#billing</c> answers billing.
/// </para>
/// </summary>
internal sealed class ChatRoutes
{
    private readonly Dictionary<string, string> _byRoom;

    /// <summary>Builds the routes over crew names already checked against the host's crews.</summary>
    public ChatRoutes(string defaultCrew, IReadOnlyDictionary<string, string>? byRoom = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultCrew);
        DefaultCrew = defaultCrew;
        _byRoom = new Dictionary<string, string>(byRoom ?? new Dictionary<string, string>(), StringComparer.Ordinal);
    }

    /// <summary>The crew a room without a route reaches.</summary>
    public string DefaultCrew { get; }

    /// <summary>The crew <paramref name="message"/>'s room reaches.</summary>
    public string Resolve(InboundMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return Resolve(message.RoomId);
    }

    /// <summary>The crew a room reaches; the default one without a room.</summary>
    public string Resolve(string? roomId) =>
        roomId is not null && _byRoom.TryGetValue(roomId, out var crew) ? crew : DefaultCrew;

    /// <summary>
    /// The hosted crews no room reaches — neither the default nor any route's target. Not an
    /// error (a crew can be hosted for another entry point), but no longer a silent one: the
    /// channel names them at startup.
    /// </summary>
    public IReadOnlyList<string> UnreachableCrews(IReadOnlyList<HostedCrewOptions> crews)
    {
        ArgumentNullException.ThrowIfNull(crews);
        var reached = new HashSet<string>(_byRoom.Values, StringComparer.OrdinalIgnoreCase) { DefaultCrew };
        return [.. crews.Select(c => c.Name).Where(name => !reached.Contains(name))];
    }

    /// <summary>
    /// Reads the routes from the channel's options, refusing — with the words to fix it — what
    /// the host cannot honour: a route key that is not a channel id, a route or a
    /// <c>DefaultCrew</c> naming a crew the host does not declare.
    /// </summary>
    /// <exception cref="HostConfigurationException">The configuration is refused.</exception>
    public static ChatRoutes From(DiscordChannelOptions options, IReadOnlyList<HostedCrewOptions> crews)
    {
        ArgumentNullException.ThrowIfNull(options);
        return From("Discord", options.DefaultCrew, options.Routes.Select(r => (r.Key, r.Value)), crews, room =>
            ulong.TryParse(room, NumberStyles.None, CultureInfo.InvariantCulture, out _)
                ? null
                : $"Discord:Routes has the key '{room}', which is not a Discord channel id (a number). "
                    + "Route a channel by its id: enable Developer Mode in Discord, then right-click the channel → Copy Channel ID.");
    }

    /// <summary>The same, for the Teams channel: its routes are a list, a room named by its Teams id.</summary>
    /// <exception cref="HostConfigurationException">The configuration is refused.</exception>
    public static ChatRoutes From(TeamsChannelOptions options, IReadOnlyList<HostedCrewOptions> crews)
    {
        ArgumentNullException.ThrowIfNull(options);
        return From("Teams", options.DefaultCrew, options.Routes.Select(r => (r.Channel, r.Crew)), crews, room =>
            room.Contains(':', StringComparison.Ordinal) && !room.Contains(';', StringComparison.Ordinal)
                ? null
                : $"Teams:Routes names the channel '{room}', which is not a Teams channel id such as '19:…@thread.tacv2'.");
    }

    private static ChatRoutes From(
        string channel,
        string? defaultCrewName,
        IEnumerable<(string Room, string Crew)> routes,
        IReadOnlyList<HostedCrewOptions> crews,
        Func<string, string?> refusalOf)
    {
        ArgumentNullException.ThrowIfNull(crews);
        if (crews.Count == 0)
            throw new HostConfigurationException(
                $"The {channel} channel is enabled but no crew is configured under '{OrkeonHostOptions.SectionName}:Crews'.");

        string? Declared(string name) =>
            crews.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))?.Name;

        var defaultCrew = crews[0].Name;
        if (!string.IsNullOrWhiteSpace(defaultCrewName))
        {
            defaultCrew = Declared(defaultCrewName)
                ?? throw new HostConfigurationException(
                    $"{channel}:DefaultCrew names '{defaultCrewName}', which is not a crew declared under "
                    + $"'{OrkeonHostOptions.SectionName}:Crews' ({Names(crews)}).");
        }

        var byRoom = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (room, crew) in routes)
        {
            if (refusalOf(room) is { } refusal)
                throw new HostConfigurationException(refusal);
            if (byRoom.ContainsKey(room))
                throw new HostConfigurationException($"{channel}:Routes names the channel '{room}' twice.");

            byRoom[room] = Declared(crew)
                ?? throw new HostConfigurationException(
                    $"{channel}:Routes sends channel '{room}' to '{crew}', which is not a crew declared under "
                    + $"'{OrkeonHostOptions.SectionName}:Crews' ({Names(crews)}).");
        }

        return new ChatRoutes(defaultCrew, byRoom);
    }

    private static string Names(IReadOnlyList<HostedCrewOptions> crews) =>
        string.Join(", ", crews.Select(c => $"'{c.Name}'"));
}
