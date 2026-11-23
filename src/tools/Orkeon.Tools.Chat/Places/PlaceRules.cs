namespace Orkeon.Tools.Chat.Places;

/// <summary>Allowed places resolved to ids: the only thing a place is compared with.</summary>
internal sealed record PlaceRules
{
    /// <summary>Whether every place is allowed.</summary>
    public bool Any { get; init; }

    /// <summary>Teams allowed whole.</summary>
    public IReadOnlySet<string> Teams { get; init; } = new HashSet<string>();

    /// <summary>Channels allowed, by team and channel id.</summary>
    public IReadOnlyList<(string Team, string Channel)> Channels { get; init; } = [];

    /// <summary>Chats allowed.</summary>
    public IReadOnlySet<string> Chats { get; init; } = new HashSet<string>();

    /// <summary>People whose one-to-one chat is allowed.</summary>
    public IReadOnlySet<string> Users { get; init; } = new HashSet<string>();

    /// <summary>Home domains whose people's one-to-one chats are allowed.</summary>
    public IReadOnlyList<string> Domains { get; init; } = [];

    /// <summary>The entries set aside, each a sentence naming it.</summary>
    public IReadOnlyList<string> Problems { get; init; } = [];

    /// <summary>Whether an entry is about people.</summary>
    public bool NamesPeople => Users.Count > 0 || Domains.Count > 0;

    /// <summary>Whether a channel is allowed.</summary>
    public bool AllowsChannel(string team, string channel) =>
        Any || Teams.Contains(team) || NamesChannel(team, channel);

    /// <summary>Whether an entry is this very channel.</summary>
    public bool NamesChannel(string team, string channel) =>
        Channels.Any(entry => SameId(entry.Team, team) && entry.Channel == channel);

    /// <summary>Whether a team holds something allowed.</summary>
    public bool ShowsTeam(string team) =>
        Any || Teams.Contains(team) || Channels.Any(entry => SameId(entry.Team, team));

    /// <summary>Whether a chat is allowed by its id.</summary>
    public bool AllowsChat(string chat) => Any || Chats.Contains(chat);

    // A directory id is a GUID, whose case means nothing.
    private static bool SameId(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
