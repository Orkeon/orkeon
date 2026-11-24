namespace Orkeon.Tools.Chat.Places;

/// <summary>
/// The shapes of the ids a call may carry into a request path. An id comes from a model: it is
/// checked against its shape, then escaped as one path segment.
/// </summary>
internal static class ChatIds
{
    private const int MaxLength = 400;
    private const string ChannelMarker = "@thread.tacv2";

    /// <summary>Whether <paramref name="value"/> is the id of a directory object (a user, a team).</summary>
    public static bool IsDirectoryId(string value) => Guid.TryParseExact(value, "D", out _);

    /// <summary>Whether <paramref name="value"/> has the shape of a chat or channel id (<c>19:...@thread.v2</c>).</summary>
    public static bool IsConversationId(string value) =>
        value.Length is > 0 and <= MaxLength
        && char.IsAsciiLetterOrDigit(value[0])
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is ':' or '@' or '.' or '_' or '-');

    /// <summary>
    /// The id of a person, a bot or a member as a result shows it: one a service made up of other characters is text
    /// to the model that reads it, and is left out.
    /// </summary>
    public static string? Shown(string? value) => value is not null && IsConversationId(value) ? value : null;

    /// <summary>Whether <paramref name="value"/> is a channel's id rather than its name.</summary>
    public static bool IsChannelId(string value) =>
        IsConversationId(value) && value.StartsWith("19:", StringComparison.Ordinal) && value.Contains("@thread.", StringComparison.Ordinal);

    /// <summary>Whether <paramref name="value"/> is the id of a chat: a channel's, which Graph may serve as a chat, is not.</summary>
    public static bool IsChatId(string value) =>
        IsConversationId(value) && !value.Contains(ChannelMarker, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether <paramref name="value"/> is a message id: the digits of its time.</summary>
    public static bool IsMessageId(string value) => value.Length is > 0 and <= 20 && value.All(char.IsAsciiDigit);
}
