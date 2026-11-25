using System.Globalization;
using System.Text.Json;

namespace Orkeon.Host.Gateway;

/// <summary>
/// The fields of a Bot Framework activity the channel reads. A body is parsed only once its token was accepted, and is still
/// read as untrusted text: every field may be missing or of another type.
/// </summary>
internal sealed record TeamsActivity
{
    private const int MaxIdLength = 1024;
    private const string FileAttachmentType = "application/vnd.microsoft.teams.file.download.info";

    public string? Type { get; private init; }
    public string? Id { get; private init; }
    public string? ChannelId { get; private init; }
    public string? ServiceUrl { get; private init; }
    public string? FromId { get; private init; }
    public string? FromRole { get; private init; }
    public string? FromObjectId { get; private init; }
    public string? RecipientId { get; private init; }
    public string? ConversationId { get; private init; }
    public string? ConversationType { get; private init; }
    public IReadOnlyList<string> Tenants { get; private init; } = [];
    public string Text { get; private init; } = string.Empty;

    /// <summary>
    /// The files of the message, by name. A channel's message shows its files as links in the HTML copy of its text:
    /// those are not read.
    /// </summary>
    public IReadOnlyList<InboundAttachment> Attachments { get; private init; } = [];

    /// <summary>Where each file of <see cref="Attachments"/> says it is, unchecked, in their order.</summary>
    public IReadOnlyList<TeamsFileAddress> Addresses { get; private init; } = [];

    /// <summary>The answer to a consent card an invoke carries, null for anything else.</summary>
    public TeamsConsentAnswer? Consent { get; private init; }

    /// <summary>Whether it comes from the bot's chat with one person.</summary>
    public bool IsPersonal => ConversationType == "personal";

    /// <summary>Reads a body, null when it is not a JSON object.</summary>
    public static TeamsActivity? Parse(ReadOnlyMemory<byte> body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object ? Read(document.RootElement) : null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // Not JSON, or a string no text can hold (a lone surrogate escape).
            return null;
        }
    }

    private static TeamsActivity Read(JsonElement root)
    {
        var from = Child(root, "from");
        var conversation = Child(root, "conversation");
        var recipientId = TextOf(Child(root, "recipient"), "id");
        string?[] tenants = [TextOf(conversation, "tenantId"), TextOf(Child(Child(root, "channelData"), "tenant"), "id")];
        var files = FilesOf(root);
        return new TeamsActivity
        {
            Type = TextOf(root, "type"),
            Id = TextOf(root, "id"),
            ChannelId = TextOf(root, "channelId"),
            ServiceUrl = TextOf(root, "serviceUrl"),
            FromId = TextOf(from, "id"),
            FromRole = TextOf(from, "role"),
            FromObjectId = TextOf(from, "aadObjectId"),
            RecipientId = recipientId,
            ConversationId = TextOf(conversation, "id"),
            ConversationType = TextOf(conversation, "conversationType"),
            Tenants = [.. tenants.OfType<string>().Where(t => t.Length > 0)],
            Text = WithoutMention(TextOf(root, "text") ?? string.Empty, root, recipientId),
            Attachments = [.. files.Select(f => f.File)],
            Addresses = [.. files.Select(f => f.Address)],
            Consent = ConsentOf(root),
        };
    }

    /// <summary>Whether the activity names its tenant, and nothing but <paramref name="tenantId"/>.</summary>
    public bool IsOfTenant(string tenantId) =>
        Tenants.Count > 0 && Tenants.All(t => string.Equals(t, tenantId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether a bot sent it: answering one is how two bots talk for ever.</summary>
    public bool IsFromBot =>
        (FromRole is not null && !string.Equals(FromRole, "user", StringComparison.OrdinalIgnoreCase))
        || (FromId is not null && (FromId.StartsWith("28:", StringComparison.Ordinal) || FromId == RecipientId));

    /// <summary>Whether the ids a reply is addressed with are there and can be written in an address.</summary>
    public bool HasUsableIds => IsUsable(Id) && IsUsable(ConversationId) && IsUsable(FromId) && IsUsable(RecipientId);

    /// <summary>
    /// The conversation a run belongs to: the thread. Teams names a channel's thread in the conversation id itself
    /// (<c>;messageid=</c>); should it not, the message opens the thread.
    /// </summary>
    public string ThreadId =>
        ConversationType == "channel" && !(ConversationId ?? string.Empty).Contains(";messageid=", StringComparison.Ordinal)
            ? $"{ConversationId};messageid={Id}"
            : ConversationId ?? string.Empty;

    /// <summary>The channel or chat the thread is in: what a route names.</summary>
    public string RoomId => (ConversationId ?? string.Empty).Split(';', 2)[0];

    /// <summary>The sender's Entra object id in its canonical form, null when the activity carries none.</summary>
    public string? SenderId => Guid.TryParseExact(FromObjectId, "D", out var id) ? id.ToString("D") : null;

    private static bool IsUsable(string? id) =>
        !string.IsNullOrWhiteSpace(id) && id.Length <= MaxIdLength && !id.Any(char.IsControl) && id.Any(c => c != '.');

    private static JsonElement Child(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var child) ? child : default;

    private static string? TextOf(JsonElement parent, string name) =>
        Child(parent, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    /// <summary>The attachments that are files or pictures: the HTML copy of the text, a card and a quote are none.</summary>
    private static List<(InboundAttachment File, TeamsFileAddress Address)> FilesOf(JsonElement root)
    {
        if (Child(root, "attachments") is not { ValueKind: JsonValueKind.Array } attachments)
            return [];

        // In a personal chat two messages may carry the same file again: each names its own.
        var message = TextOf(Child(root, "conversation"), "conversationType") == "personal" ? TextOf(root, "id") : null;
        var files = new List<(InboundAttachment File, TeamsFileAddress Address)>();
        foreach (var attachment in attachments.EnumerateArray())
        {
            var type = TextOf(attachment, "contentType");
            var picture = type?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true;
            if (!picture && !string.Equals(type, FileAttachmentType, StringComparison.OrdinalIgnoreCase))
                continue;

            var content = Child(attachment, "content");
            var index = files.Count.ToString(CultureInfo.InvariantCulture);
            files.Add((
                new InboundAttachment
                {
                    Id = message is null ? TextOf(content, "uniqueId") ?? index : $"{message}/{index}",
                    Name = TextOf(attachment, "name") ?? (picture ? "image" : string.Empty),
                    ContentType = picture ? type : null,
                },
                new TeamsFileAddress(picture ? TextOf(attachment, "contentUrl") : TextOf(content, "downloadUrl"), TextOf(content, "tenantId"), picture)));
        }

        return files;
    }

    private static TeamsConsentAnswer? ConsentOf(JsonElement root)
    {
        if (TextOf(root, "type") != "invoke" || TextOf(root, "name") != "fileConsent/invoke")
            return null;

        var value = Child(root, "value");
        var upload = Child(value, "uploadInfo");
        return new TeamsConsentAnswer
        {
            Action = TextOf(value, "action"),
            FileId = TextOf(Child(value, "context"), "fileId"),
            Upload = new TeamsFileAddress(TextOf(upload, "uploadUrl"), TextOf(upload, "tenantId"), picture: false),
            ContentUrl = TextOf(upload, "contentUrl"),
            UniqueId = TextOf(upload, "uniqueId"),
            FileType = TextOf(upload, "fileType"),
        };
    }

    /// <summary>The message without the mention of the bot Teams puts in it; other people's mentions stay.</summary>
    private static string WithoutMention(string text, JsonElement root, string? botId)
    {
        if (botId is not null && Child(root, "entities") is { ValueKind: JsonValueKind.Array } entities)
        {
            foreach (var entity in entities.EnumerateArray())
            {
                if (TextOf(entity, "type") == "mention"
                    && TextOf(Child(entity, "mentioned"), "id") == botId
                    && TextOf(entity, "text") is { Length: > 0 } mention)
                {
                    text = text.Replace(mention, " ", StringComparison.Ordinal);
                }
            }
        }

        return text.Replace("&nbsp;", " ", StringComparison.Ordinal).Trim();
    }
}

/// <summary>Where Teams says a file is, unchecked. Not a record: nothing that prints it prints the address.</summary>
internal sealed class TeamsFileAddress(string? written, string? tenantId, bool picture)
{
    /// <summary>The address, exactly as received.</summary>
    public string? Written { get; } = written;

    /// <summary>The tenant the file is of, when it is said.</summary>
    public string? TenantId { get; } = tenantId;

    /// <summary>Whether it is a picture of the message, kept by the Bot Connector.</summary>
    public bool Picture { get; } = picture;
}

/// <summary>What a person answered to a consent card, unchecked. Not a record: nothing that prints it prints an address.</summary>
internal sealed class TeamsConsentAnswer
{
    public string? Action { get; init; }
    public string? FileId { get; init; }
    public required TeamsFileAddress Upload { get; init; }
    public string? ContentUrl { get; init; }
    public string? UniqueId { get; init; }
    public string? FileType { get; init; }
}
