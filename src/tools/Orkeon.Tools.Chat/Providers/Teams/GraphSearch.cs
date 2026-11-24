using System.Text;
using System.Text.Json;
using Orkeon.Tools.Chat.Places;

namespace Orkeon.Tools.Chat.Providers.Teams;

/// <summary>
/// The search of messages, in and out of Graph. The words come from a model: each is a quoted
/// term, so an operator, a property or a parenthesis in it is text, and nothing that could close
/// the quotes is kept (the query language documents no escape).
/// </summary>
internal static class GraphSearch
{
    private const string TeamsParent = "parentMessageId";

    /// <summary>The query of <paramref name="query"/>: terms side by side, all of which must hold.</summary>
    public static string QueryString(ChatSearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var terms = query.Words.Select(Literal).Where(word => word.Length > 0).Select(word => $"\"{word}\"").ToList();
        if (query.From is { } from)
            terms.Add($"from:\"{Literal(from)}\"");
        if (query.Mentions is { } person)
            terms.Add($"mentions:{Guid.ParseExact(person, "D"):N}");
        return string.Join(' ', terms);
    }

    /// <summary>
    /// A word that stays inside its quotes: letters, digits, marks and the punctuation that joins
    /// words. The rest (quotes of every script, backslash, wildcard, controls) is a space.
    /// </summary>
    internal static string Literal(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        var kept = new StringBuilder(word.Length);
        foreach (var rune in word.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            var keep = Rune.IsLetterOrDigit(rune)
                || category is System.Globalization.UnicodeCategory.NonSpacingMark or System.Globalization.UnicodeCategory.SpacingCombiningMark
                || (rune.IsAscii && "._-@#+'/&%$:=".Contains((char)rune.Value, StringComparison.Ordinal));
            kept.Append(keep ? rune.ToString() : " ");
        }

        var literal = string.Join(' ', kept.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return literal.EnumerateRunes().Any(Rune.IsLetterOrDigit) ? literal : string.Empty;
    }

    /// <summary>The hits of an answer, as ids: a hit whose ids have not their shapes is dropped, its summary is never read.</summary>
    public static ChatSearchPage Read(JsonElement json)
    {
        var hits = new List<ChatSearchHit>();
        var more = false;
        foreach (var container in GraphJson.Items(json).SelectMany(answer => GraphJson.Items(answer, "hitsContainers")))
        {
            more |= container.TryGetProperty("moreResultsAvailable", out var available) && available.ValueKind == JsonValueKind.True;
            hits.AddRange(GraphJson.Items(container, "hits").Select(ReadHit).OfType<ChatSearchHit>());
        }

        return new ChatSearchPage(hits, more);
    }

    private static ChatSearchHit? ReadHit(JsonElement hit)
    {
        if (GraphJson.Object(hit, "resource") is not { } message || GraphJson.Text(message, "id") is not { } id || !ChatIds.IsMessageId(id))
            return null;
        if (GraphJson.Object(message, "channelIdentity") is { } channel && GraphJson.Text(channel, "channelId") is { Length: > 0 } channelId)
        {
            return GraphJson.Text(channel, "teamId") is { } team && ChatIds.IsDirectoryId(team) && ChatIds.IsChannelId(channelId)
                ? new ChatSearchHit(new ChatTarget { TeamId = team, ChannelId = channelId }, Parent(message, id), id)
                : null;
        }

        return GraphJson.Text(message, "chatId") is { } chat && ChatIds.IsConversationId(chat)
            ? new ChatSearchHit(new ChatTarget { ChatId = chat }, null, id)
            : null;
    }

    // Graph's hit holds no replyToId: the link of a reply names its thread.
    private static string? Parent(JsonElement message, string id)
    {
        if ((GraphJson.Text(message, "webLink") ?? GraphJson.Text(message, "webUrl")) is not { } link || !Uri.TryCreate(link, UriKind.Absolute, out var address))
            return null;
        return address.Query.TrimStart('?').Split('&')
            .Select(pair => pair.Split('=', 2))
            .Where(pair => pair.Length == 2 && pair[0] == TeamsParent && pair[1] != id && ChatIds.IsMessageId(pair[1]))
            .Select(pair => pair[1])
            .FirstOrDefault();
    }
}
