using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Orkeon.Tools.Chat.Text;

namespace Orkeon.Tools.Chat.Providers.Teams;

/// <summary>The messages of Teams, in and out of Graph's JSON.</summary>
internal static class GraphMessages
{
    private static readonly string[] ExternalIdentities =
        ["anonymousGuest", "federatedUser", "personalMicrosoftAccountUser", "skypeUser", "phoneUser", "emailUser"];

    /// <summary>Reads a <c>chatMessage</c>.</summary>
    public static ChatMessage Read(JsonElement json, bool isChannel)
    {
        var html = string.Empty;
        var isHtml = false;
        if (GraphJson.Object(json, "body") is { } body)
        {
            html = GraphJson.Text(body, "content") ?? string.Empty;
            isHtml = string.Equals(GraphJson.Text(body, "contentType"), "html", StringComparison.OrdinalIgnoreCase);
        }

        var rendered = isHtml ? HtmlTextRenderer.Render(html) : new RenderedHtml(html, HadHiddenContent: false);
        var subject = GraphJson.Text(json, "subject");
        return new ChatMessage
        {
            Id = GraphJson.Text(json, "id") ?? string.Empty,
            Author = ReadAuthor(json),
            Created = GraphJson.Date(json, "createdDateTime"),
            Edited = GraphJson.Date(json, "lastEditedDateTime"),
            Text = string.IsNullOrWhiteSpace(subject) ? rendered.Text : $"{subject.Trim()}\n{rendered.Text}",
            HasImages = isHtml && html.Contains("<img", StringComparison.OrdinalIgnoreCase),
            HadHiddenText = rendered.HadHiddenContent,
            IsMessage = GraphJson.Text(json, "messageType") is null or "message",
            IsDeleted = GraphJson.Text(json, "deletedDateTime") is not null,
            ViolatesPolicy = GraphJson.Object(json, "policyViolation") is not null,
            Mentions = GraphJson.Items(json, "mentions").Select(ReadMention).ToList(),
            Reactions = GraphJson.Items(json, "reactions")
                .Select(reaction => GraphJson.Text(reaction, "displayName") ?? GraphJson.Text(reaction, "reactionType"))
                .OfType<string>()
                .GroupBy(reaction => reaction, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
            Attachments = GraphJson.Items(json, "attachments")
                .Select(attachment => GraphJson.Text(attachment, "name") ?? GraphJson.Text(attachment, "contentType") ?? "attachment")
                .ToList(),
            Replies = isChannel && GraphJson.Text(json, "replyToId") is null ? GraphJson.Items(json, "replies").Count() : null,
            MoreReplies = GraphJson.Text(json, "replies@odata.nextLink") is not null,
        };
    }

    /// <summary>
    /// The body of a post. The mentions are written here from people the directory returned; the
    /// text of the message can hold none, its markup is encoded.
    /// </summary>
    public static JsonObject Write(ChatOutgoing message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var html = new StringBuilder();
        var mentions = new JsonArray();
        foreach (var person in message.Mentions)
        {
            var index = mentions.Count;
            var name = string.IsNullOrWhiteSpace(person.Name) ? "someone" : person.Name.Trim();
            html.Append("<at id=\"").Append(index).Append("\">").Append(WebUtility.HtmlEncode(name)).Append("</at> ");
            mentions.Add(new JsonObject
            {
                ["id"] = index,
                ["mentionText"] = name,
                ["mentioned"] = new JsonObject
                {
                    ["user"] = new JsonObject { ["id"] = person.Id, ["displayName"] = name, ["userIdentityType"] = "aadUser" },
                },
            });
        }

        html.Append(message.Html);
        var post = new JsonObject { ["body"] = new JsonObject { ["contentType"] = "html", ["content"] = html.ToString() } };
        if (mentions.Count > 0)
            post["mentions"] = mentions;
        return post;
    }

    private static ChatAuthor? ReadAuthor(JsonElement json)
    {
        if (GraphJson.Object(json, "from") is not { } from)
            return null;
        if (GraphJson.Object(from, "user") is { } user)
        {
            return new ChatAuthor(
                GraphJson.Text(user, "id"),
                GraphJson.Text(user, "displayName"),
                IsApplication: false,
                IsExternal: ExternalIdentities.Contains(GraphJson.Text(user, "userIdentityType")));
        }

        return GraphJson.Object(from, "application") is { } application
            ? new ChatAuthor(GraphJson.Text(application, "id"), GraphJson.Text(application, "displayName"), IsApplication: true, IsExternal: false)
            : null;
    }

    private static ChatMention ReadMention(JsonElement mention)
    {
        string? id = null;
        if (GraphJson.Object(mention, "mentioned") is { } mentioned && GraphJson.Object(mentioned, "user") is { } user)
            id = GraphJson.Text(user, "id");
        return new ChatMention(id, GraphJson.Text(mention, "mentionText") ?? string.Empty);
    }
}
