using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Tools.Chat.Places;

/// <summary>What a written place designates.</summary>
internal enum PlaceForm
{
    /// <summary><c>*</c>: every place the account reaches.</summary>
    Any,

    /// <summary><c>team:T</c>: every channel of a team.</summary>
    Team,

    /// <summary><c>team:T/channel:C</c>.</summary>
    Channel,

    /// <summary><c>chat:ID</c>: a group or a meeting chat, or a one-to-one chat by its id.</summary>
    Chat,

    /// <summary><c>user:ADDRESS</c>: the one-to-one chat with a person.</summary>
    User,

    /// <summary><c>user:*@DOMAIN</c>: the one-to-one chat with anyone of a domain.</summary>
    Domain,
}

/// <summary>
/// A place as the settings and the calls write it, one syntax for both. It is only text here:
/// names become ids in <see cref="PlaceGate"/>.
/// </summary>
internal sealed record PlaceReference
{
    /// <summary>The accepted forms, for messages.</summary>
    public const string Syntax = "write team:<team>, team:<team>/channel:<channel>, chat:<id>, user:<address>, user:*@<domain> or *";

    private const string TeamPrefix = "team:";
    private const string ChannelSeparator = "/channel:";
    private const string ChatPrefix = "chat:";
    private const string UserPrefix = "user:";
    private const int MaxLength = 1000;

    private PlaceReference(string text, PlaceForm form, string value, string channel = "")
    {
        Text = text;
        Form = form;
        Value = value;
        Channel = channel;
    }

    /// <summary>The place as written.</summary>
    public string Text { get; }

    /// <summary>What it designates.</summary>
    public PlaceForm Form { get; }

    /// <summary>The team (name or id), the chat id, the address or user id, or the domain.</summary>
    public string Value { get; }

    /// <summary>The channel (name or id) of <see cref="PlaceForm.Channel"/>; empty otherwise.</summary>
    public string Channel { get; }

    /// <summary>Reads <paramref name="text"/>; false when it is not a place.</summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out PlaceReference? place)
    {
        place = null;
        var written = text?.Trim();
        if (string.IsNullOrEmpty(written) || written.Length > MaxLength || written.Any(char.IsControl))
            return false;

        if (written == "*")
            place = new PlaceReference(written, PlaceForm.Any, written);
        else if (written.StartsWith(TeamPrefix, StringComparison.OrdinalIgnoreCase))
            place = ParseTeam(written);
        else if (written.StartsWith(ChatPrefix, StringComparison.OrdinalIgnoreCase))
            place = Of(written, PlaceForm.Chat, written[ChatPrefix.Length..].Trim(), ChatIds.IsChatId);
        else if (written.StartsWith(UserPrefix, StringComparison.OrdinalIgnoreCase))
            place = ParseUser(written);
        return place is not null;
    }

    private static PlaceReference? ParseTeam(string written)
    {
        var rest = written[TeamPrefix.Length..];
        var cut = rest.LastIndexOf(ChannelSeparator, StringComparison.OrdinalIgnoreCase);
        var team = (cut < 0 ? rest : rest[..cut]).Trim();
        if (cut < 0)
            return Of(written, PlaceForm.Team, team, value => value != "*");

        var channel = rest[(cut + ChannelSeparator.Length)..].Trim();
        return team.Length == 0 || team == "*" || channel.Length == 0 || channel == "*"
            ? null
            : new PlaceReference(written, PlaceForm.Channel, team, channel);
    }

    private static PlaceReference? ParseUser(string written)
    {
        var who = written[UserPrefix.Length..].Trim();
        if (who.StartsWith("*@", StringComparison.Ordinal))
            return Of(written, PlaceForm.Domain, who[2..], IsDomain);
        return Of(written, PlaceForm.User, who, value => ChatIds.IsDirectoryId(value) || IsAddress(value));
    }

    private static PlaceReference? Of(string written, PlaceForm form, string value, Func<string, bool> accepts) =>
        value.Length > 0 && accepts(value) ? new PlaceReference(written, form, value) : null;

    private static bool IsAddress(string value)
    {
        var at = value.IndexOf('@', StringComparison.Ordinal);
        return at > 0 && at == value.LastIndexOf('@') && at < value.Length - 1
            && !value.Any(c => char.IsWhiteSpace(c) || c is '*' or '/' or '\\' or '?' or '<' or '>');
    }

    private static bool IsDomain(string value) =>
        value.Length > 2 && char.IsLetterOrDigit(value[0]) && char.IsLetterOrDigit(value[^1])
        && value.Contains('.', StringComparison.Ordinal)
        && value.All(c => char.IsLetterOrDigit(c) || c is '.' or '-');
}
