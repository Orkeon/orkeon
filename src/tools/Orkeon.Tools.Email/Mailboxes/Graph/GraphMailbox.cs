using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MimeKit;
using Orkeon.Tools.Email.Configuration;

namespace Orkeon.Tools.Email.Mailboxes.Graph;

/// <summary>
/// An Outlook.com / Microsoft 365 mailbox through Microsoft Graph. Messages are read as MIME
/// (<c>$value</c>) so every backend maps them with the same code; ids are immutable, so a moved
/// message keeps its id.
/// </summary>
internal sealed class GraphMailbox : IMailbox
{
    private const string CursorPrefix = "n:";
    private const string SummarySelect = "id,subject,from,receivedDateTime,isRead,flag,hasAttachments,bodyPreview";
    private const string FolderSelect = "id,displayName,parentFolderId,childFolderCount,totalItemCount,unreadItemCount";
    private const int MaxFolders = 1000;
    private const int MaxDepth = 8;
    private static readonly TimeSpan FolderCacheLifetime = TimeSpan.FromMinutes(1);

    private static readonly (string Role, string WellKnown)[] WellKnownFolders =
    [
        (FolderRoles.Inbox, "inbox"),
        (FolderRoles.Sent, "sentitems"),
        (FolderRoles.Drafts, "drafts"),
        (FolderRoles.Trash, "deleteditems"),
        (FolderRoles.Junk, "junkemail"),
        (FolderRoles.Archive, "archive"),
    ];

    private readonly GraphClient _graph;
    private readonly TimeProvider _time;
    private IReadOnlyList<GraphFolder>? _folders;
    private DateTimeOffset _foldersLoadedAt;

    /// <summary>Creates the mailbox over <paramref name="graph"/>.</summary>
    public GraphMailbox(GraphClient graph, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(time);
        _graph = graph;
        _time = time;
    }

    /// <inheritdoc />
    public MailboxCapabilities Capabilities =>
        MailboxCapabilities.Folders | MailboxCapabilities.Move | MailboxCapabilities.Flags | MailboxCapabilities.Drafts
        | MailboxCapabilities.BodySearch | MailboxCapabilities.RawQuery | MailboxCapabilities.Trash;

    /// <inheritdoc />
    public async Task<IReadOnlyList<MailFolderInfo>> ListFoldersAsync(CancellationToken cancellationToken)
    {
        var folders = await LoadFoldersAsync(refresh: true, cancellationToken).ConfigureAwait(false);
        return folders
            .Select(folder => folder.Info)
            .OrderBy(folder => folder.Role == FolderRoles.Inbox ? 0 : 1)
            .ThenBy(folder => folder.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<(MailFolderInfo Folder, bool Created)> CreateFolderAsync(string path, CancellationToken cancellationToken)
    {
        var segments = SplitPath(path);
        var folders = await LoadFoldersAsync(refresh: true, cancellationToken).ConfigureAwait(false);
        if (Find(folders, string.Join('/', segments)) is { } existing)
            return (existing.Info, false);

        string? parentId = null;
        GraphFolder? created = null;
        for (var depth = 0; depth < segments.Length; depth++)
        {
            var prefix = string.Join('/', segments[..(depth + 1)]);
            if (Find(folders, prefix) is { } present)
            {
                parentId = present.Id;
                continue;
            }

            var address = GraphClient.Resolve(parentId is null ? "me/mailFolders" : $"me/mailFolders/{Escape(parentId)}/childFolders");
            using var document = await _graph.SendJsonAsync(HttpMethod.Post, address, new JsonObject { ["displayName"] = segments[depth] }, cancellationToken)
                .ConfigureAwait(false);
            created = ReadFolder(document.RootElement, prefix, role: null);
            parentId = created.Id;
        }

        _folders = null;
        return (created?.Info ?? new MailFolderInfo(string.Join('/', segments), segments[^1], null, 0, 0), true);
    }

    /// <inheritdoc />
    public async Task<MailFolderInfo> RenameFolderAsync(string path, string newName, CancellationToken cancellationToken)
    {
        var folder = await ResolveFolderAsync(path, cancellationToken).ConfigureAwait(false);
        if (folder.Info.Role is { } role)
            throw new EmailToolException(EmailErrorCode.InvalidRequest, $"The {role} folder is a system folder and cannot be renamed.");

        var name = newName.Trim();
        if (name.Length == 0 || name.Contains('/', StringComparison.Ordinal))
            throw new EmailToolException(EmailErrorCode.InvalidRequest, "`new_name` is a single folder name, without '/'.");

        using var document = await _graph.SendJsonAsync(
            HttpMethod.Patch, GraphClient.Resolve($"me/mailFolders/{Escape(folder.Id)}"), new JsonObject { ["displayName"] = name }, cancellationToken)
            .ConfigureAwait(false);
        _folders = null;
        var cut = folder.Info.Path.LastIndexOf('/');
        var newPath = cut < 0 ? name : folder.Info.Path[..(cut + 1)] + name;
        return ReadFolder(document.RootElement, newPath, role: null).Info;
    }

    /// <inheritdoc />
    public async Task<MessagePage> SearchAsync(MailSearch search, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(search);
        Uri address;
        if (!string.IsNullOrWhiteSpace(search.Cursor))
        {
            address = ParseCursor(search.Cursor);
        }
        else
        {
            var folder = await ResolveFolderAsync(search.Folder, cancellationToken).ConfigureAwait(false);
            address = GraphClient.Resolve(GraphQueries.MessagesQuery(folder.Id, search, SummarySelect));
        }

        using var document = await _graph.GetJsonAsync(address, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var textual = GraphQueries.UsesSearch(search);
        var messages = new List<MessageSummaryInfo>();
        if (root.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                var summary = Summarize(item);
                if (!textual || GraphQueries.PostFilter(search, summary))
                    messages.Add(summary);
            }
        }

        var next = root.TryGetProperty("@odata.nextLink", out var link) && link.GetString() is { } nextLink
            ? CursorPrefix + Base64Url(nextLink)
            : null;
        return new MessagePage(messages, next);
    }

    /// <inheritdoc />
    public async Task<FetchedMessage> GetMessageAsync(string id, CancellationToken cancellationToken)
    {
        var graphId = MessageIds.ParseGraph(id);
        using var meta = await _graph.GetJsonAsync(
            GraphClient.Resolve($"me/messages/{Escape(graphId)}?$select=isRead,flag,parentFolderId"), cancellationToken).ConfigureAwait(false);
        var root = meta.RootElement;
        var folders = await LoadFoldersAsync(refresh: false, cancellationToken).ConfigureAwait(false);
        var parentId = ReadString(root, "parentFolderId");
        var folderPath = folders.FirstOrDefault(folder => folder.Id == parentId)?.Info.Path ?? string.Empty;

        var message = await _graph.GetMimeAsync(GraphClient.Resolve($"me/messages/{Escape(graphId)}/$value"), cancellationToken).ConfigureAwait(false);
        return new FetchedMessage(id, folderPath, message, ReadBool(root, "isRead"), IsFlagged(root));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MovedMessage>> MoveAsync(IReadOnlyList<string> ids, string destination, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var target = await ResolveFolderAsync(destination, cancellationToken).ConfigureAwait(false);
        var moved = new List<MovedMessage>(ids.Count);
        foreach (var id in ids)
        {
            var graphId = MessageIds.ParseGraph(id);
            using var document = await _graph.SendJsonAsync(
                HttpMethod.Post, GraphClient.Resolve($"me/messages/{Escape(graphId)}/move"), new JsonObject { ["destinationId"] = target.Id }, cancellationToken)
                .ConfigureAwait(false);
            var newId = ReadString(document.RootElement, "id");
            moved.Add(new MovedMessage(id, newId is null ? id : MessageIds.Graph(newId)));
        }

        return moved;
    }

    /// <inheritdoc />
    public async Task<int> SetFlagsAsync(IReadOnlyList<string> ids, bool? seen, bool? flagged, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var patch = new JsonObject();
        if (seen is { } read)
            patch["isRead"] = read;
        if (flagged is { } star)
            patch["flag"] = new JsonObject { ["flagStatus"] = star ? "flagged" : "notFlagged" };

        foreach (var id in ids)
        {
            using var _ = await _graph.SendJsonAsync(
                HttpMethod.Patch, GraphClient.Resolve($"me/messages/{Escape(MessageIds.ParseGraph(id))}"), patch.DeepClone(), cancellationToken)
                .ConfigureAwait(false);
        }

        return ids.Count;
    }

    /// <inheritdoc />
    public async Task<DeleteOutcome> DeleteAsync(IReadOnlyList<string> ids, bool permanent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (permanent)
        {
            foreach (var id in ids)
            {
                using var _ = await _graph.SendJsonAsync(
                    HttpMethod.Post, GraphClient.Resolve($"me/messages/{Escape(MessageIds.ParseGraph(id))}/permanentDelete"), null, cancellationToken)
                    .ConfigureAwait(false);
            }

            return new DeleteOutcome(ids.Count, true, null);
        }

        var trash = await ResolveFolderAsync(FolderRoles.Trash, cancellationToken).ConfigureAwait(false);
        foreach (var id in ids)
        {
            var graphId = MessageIds.ParseGraph(id);
            using var meta = await _graph.GetJsonAsync(GraphClient.Resolve($"me/messages/{Escape(graphId)}?$select=parentFolderId"), cancellationToken)
                .ConfigureAwait(false);
            if (ReadString(meta.RootElement, "parentFolderId") == trash.Id)
            {
                throw new EmailToolException(
                    EmailErrorCode.InvalidRequest,
                    "This message is already in Deleted Items: pass `permanent: true` to delete it for good (needs the Purge right).");
            }

            using var _ = await _graph.SendJsonAsync(
                HttpMethod.Post, GraphClient.Resolve($"me/messages/{Escape(graphId)}/move"), new JsonObject { ["destinationId"] = trash.Id }, cancellationToken)
                .ConfigureAwait(false);
        }

        return new DeleteOutcome(ids.Count, false, trash.Info.Path);
    }

    /// <inheritdoc />
    public async Task<(string? Id, string Folder)> SaveDraftAsync(MimeMessage message, CancellationToken cancellationToken)
    {
        using var document = await _graph.PostMimeAsync(GraphClient.Resolve("me/messages"), message, cancellationToken).ConfigureAwait(false);
        var id = ReadString(document.RootElement, "id");
        var drafts = await ResolveFolderAsync(FolderRoles.Drafts, cancellationToken).ConfigureAwait(false);
        return (id is null ? null : MessageIds.Graph(id), drafts.Info.Path);
    }

    /// <inheritdoc />
    public Task AppendToSentAsync(MimeMessage message, CancellationToken cancellationToken) =>
        Task.CompletedTask; // sendMail files the message in Sent Items itself.

    private async Task<GraphFolder> ResolveFolderAsync(string folder, CancellationToken cancellationToken)
    {
        var folders = await LoadFoldersAsync(refresh: false, cancellationToken).ConfigureAwait(false);
        if (FolderRoles.Parse(folder) is { } role)
        {
            return folders.FirstOrDefault(candidate => candidate.Info.Role == role)
                ?? throw new EmailToolException(EmailErrorCode.FolderNotFound, $"This account has no {role} folder: list them with email_folders.");
        }

        return Find(folders, string.Join('/', SplitPath(folder)))
            ?? throw new EmailToolException(EmailErrorCode.FolderNotFound, $"The folder '{folder}' does not exist: list them with email_folders.");
    }

    private static GraphFolder? Find(IReadOnlyList<GraphFolder> folders, string path) =>
        folders.FirstOrDefault(folder => string.Equals(folder.Info.Path, path, StringComparison.OrdinalIgnoreCase));

    private async Task<IReadOnlyList<GraphFolder>> LoadFoldersAsync(bool refresh, CancellationToken cancellationToken)
    {
        {
            if (!refresh && _folders is not null && _time.GetUtcNow() - _foldersLoadedAt < FolderCacheLifetime)
                return _folders;

            var roles = await LoadRolesAsync(cancellationToken).ConfigureAwait(false);
            var folders = new List<GraphFolder>();
            var pending = new Queue<(string? ParentId, string Prefix, int Depth)>();
            pending.Enqueue((null, string.Empty, 0));
            while (pending.Count > 0 && folders.Count < MaxFolders)
            {
                var (parentId, prefix, depth) = pending.Dequeue();
                var relative = parentId is null
                    ? $"me/mailFolders?$top=250&$select={FolderSelect}"
                    : $"me/mailFolders/{Escape(parentId)}/childFolders?$top=250&$select={FolderSelect}";
                Uri? address = GraphClient.Resolve(relative);
                while (address is not null && folders.Count < MaxFolders)
                {
                    using var document = await _graph.GetJsonAsync(address, cancellationToken).ConfigureAwait(false);
                    address = NextLink(document.RootElement);
                    if (!document.RootElement.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
                        break;

                    foreach (var item in value.EnumerateArray())
                    {
                        var name = ReadString(item, "displayName") ?? string.Empty;
                        var path = prefix.Length == 0 ? name : $"{prefix}/{name}";
                        var id = ReadString(item, "id") ?? string.Empty;
                        var folder = ReadFolder(item, path, roles.GetValueOrDefault(id));
                        folders.Add(folder);
                        if (ReadInt(item, "childFolderCount") > 0 && depth + 1 < MaxDepth)
                            pending.Enqueue((folder.Id, path, depth + 1));
                    }
                }
            }

            _folders = folders;
            _foldersLoadedAt = _time.GetUtcNow();
            return folders;
        }
    }

    private async Task<Dictionary<string, string>> LoadRolesAsync(CancellationToken cancellationToken)
    {
        var roles = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (role, wellKnown) in WellKnownFolders)
        {
            try
            {
                using var document = await _graph.GetJsonAsync(GraphClient.Resolve($"me/mailFolders/{wellKnown}?$select=id"), cancellationToken)
                    .ConfigureAwait(false);
                if (ReadString(document.RootElement, "id") is { } id)
                    roles[id] = role;
            }
            catch (EmailToolException ex) when (ex.Code == EmailErrorCode.MessageNotFound)
            {
                // Not every mailbox has every well-known folder (Archive is created on first use).
            }
        }

        return roles;
    }

    private static Uri? NextLink(JsonElement root)
    {
        if (!root.TryGetProperty("@odata.nextLink", out var link) || link.GetString() is not { } text)
            return null;
        return Uri.TryCreate(text, UriKind.Absolute, out var next) && GraphClient.IsGraphAddress(next) ? next : null;
    }

    private static Uri ParseCursor(string cursor)
    {
        if (cursor.StartsWith(CursorPrefix, StringComparison.Ordinal)
            && TryFromBase64Url(cursor[CursorPrefix.Length..], out var text)
            && Uri.TryCreate(text, UriKind.Absolute, out var next)
            && GraphClient.IsGraphAddress(next))
        {
            return next;
        }

        throw new EmailToolException(EmailErrorCode.InvalidRequest, "`cursor` is not a cursor of this account: pass `next_cursor` exactly as a previous page returned it.");
    }

    private static MessageSummaryInfo Summarize(JsonElement item)
    {
        var sender = item.TryGetProperty("from", out var from) && from.TryGetProperty("emailAddress", out var address)
            ? FormatAddress(address)
            : string.Empty;
        return new MessageSummaryInfo
        {
            Id = MessageIds.Graph(ReadString(item, "id") ?? string.Empty),
            From = sender,
            Subject = ReadString(item, "subject") ?? string.Empty,
            Date = ReadDate(item, "receivedDateTime"),
            Seen = ReadBool(item, "isRead"),
            Flagged = IsFlagged(item),
            HasAttachments = ReadBool(item, "hasAttachments"),
            Preview = Previews.Shorten(ReadString(item, "bodyPreview")),
        };
    }

    private static GraphFolder ReadFolder(JsonElement item, string path, string? role)
    {
        var name = ReadString(item, "displayName") ?? path;
        return new GraphFolder(
            ReadString(item, "id") ?? string.Empty,
            new MailFolderInfo(path, name, role, ReadInt(item, "totalItemCount"), ReadInt(item, "unreadItemCount")));
    }

    private static string FormatAddress(JsonElement emailAddress)
    {
        var name = ReadString(emailAddress, "name");
        var address = ReadString(emailAddress, "address") ?? string.Empty;
        return string.IsNullOrWhiteSpace(name) || string.Equals(name, address, StringComparison.OrdinalIgnoreCase)
            ? address
            : $"{name} <{address}>";
    }

    private static bool? IsFlagged(JsonElement item) =>
        item.TryGetProperty("flag", out var flag) && flag.ValueKind == JsonValueKind.Object
            ? string.Equals(ReadString(flag, "flagStatus"), "flagged", StringComparison.OrdinalIgnoreCase)
            : null;

    private static string? ReadString(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? ReadBool(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static int? ReadInt(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) && value.TryGetInt32(out var number)
            ? number
            : null;

    private static DateTimeOffset? ReadDate(JsonElement item, string name) =>
        ReadString(item, name) is { } text
        && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            ? date
            : null;

    private static string[] SplitPath(string path)
    {
        var segments = path.Trim().Trim('/').Split('/', StringSplitOptions.TrimEntries);
        if (segments.Length == 0 || segments.Any(segment => segment.Length == 0 || segment.Any(char.IsControl)))
            throw new EmailToolException(EmailErrorCode.InvalidRequest, $"'{path}' is not a folder path: segments separated by '/', none empty.");
        return segments;
    }

    private static string Escape(string id) => Uri.EscapeDataString(id);

    private static string Base64Url(string text) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool TryFromBase64Url(string value, out string decoded)
    {
        decoded = string.Empty;
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
        var buffer = new byte[base64.Length];
        if (!Convert.TryFromBase64String(base64, buffer, out var written))
            return false;
        decoded = Encoding.UTF8.GetString(buffer, 0, written);
        return true;
    }

    /// <summary>A Graph folder: its id and how the tools present it.</summary>
    private sealed record GraphFolder(string Id, MailFolderInfo Info);
}
