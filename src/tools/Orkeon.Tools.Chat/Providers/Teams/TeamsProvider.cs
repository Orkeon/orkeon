using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Orkeon.Tools.Chat.Configuration;
using Orkeon.Tools.Chat.Constants;
using Orkeon.Tools.Chat.Places;

namespace Orkeon.Tools.Chat.Providers.Teams;

/// <summary>
/// Microsoft Teams through Microsoft Graph v1.0, as the signed-in user — or as an application, which
/// is nobody: it lists the places it was allowed, and looks nobody up.
/// </summary>
internal sealed class TeamsProvider : IChatProvider
{
    private const string PersonFields = "$select=id,displayName,userPrincipalName,mail,userType,creationType";

    private const string NoDirectory = "looks nobody up: the ids of a place's members come from chat_members with `place`";

    private const string OnlyReads = "reads as itself and has no message of its own";

    private readonly GraphClient _graph;
    private readonly ConcurrentDictionary<string, ChatPerson> _people = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ChatTeam> _allowedTeams = new(StringComparer.OrdinalIgnoreCase);
    private ChatPerson? _self;

    /// <summary>Creates the provider over <paramref name="graph"/>.</summary>
    public TeamsProvider(GraphClient graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        _graph = graph;
    }

    /// <inheritdoc />
    public async Task<ChatPerson> GetSelfAsync(CancellationToken cancellationToken)
    {
        if (_self is { } known)
            return known;
        RefuseApplication("has no user of its own");
        var json = await _graph.GetAsync($"me?{PersonFields}", ChatScopes.UserRead, cancellationToken).ConfigureAwait(false);
        var self = ReadPerson(json) ?? throw Unexpected("the signed-in user");
        _self = self;
        return self;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ChatTeam>> ListTeamsAsync(CancellationToken cancellationToken) => _graph.Account.IsApplication
        ? AllowedTeamsAsync(cancellationToken)
        : ListAsync("me/joinedTeams", string.Empty, ChatScopes.TeamRead, team => Named(team, (id, name) => new ChatTeam(id, name)), cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<ChatChannel>> ListChannelsAsync(string teamId, CancellationToken cancellationToken) =>
        ListAsync(
            $"teams/{GraphClient.Segment(teamId)}/channels",
            "$select=id,displayName,membershipType",
            ChatScopes.ChannelRead,
            channel => Named(channel, (id, name) => new ChatChannel(id, name, GraphJson.Text(channel, "membershipType"))),
            cancellationToken);

    /// <inheritdoc />
    public async Task<ChatPage<ChatConversation>> ListChatsAsync(int top, string? page, CancellationToken cancellationToken)
    {
        if (_graph.Account.IsApplication)
            return await AllowedChatsAsync(top, page, cancellationToken).ConfigureAwait(false);
        var query = string.Create(CultureInfo.InvariantCulture, $"$expand=members&$top={top}");
        var json = await _graph.GetAsync(Address("me/chats", query, page), ChatScopes.ChatRead, cancellationToken).ConfigureAwait(false);
        var chats = GraphJson.Items(json).Select(ReadChat).Where(chat => chat.Id.Length > 0).ToList();
        return new ChatPage<ChatConversation>(chats, _graph.NextPage(json));
    }

    /// <inheritdoc />
    public async Task<ChatConversation> GetChatAsync(string chatId, CancellationToken cancellationToken)
    {
        var json = await _graph.GetAsync($"chats/{GraphClient.Segment(chatId)}?$expand=members", ChatScopes.ChatRead, cancellationToken).ConfigureAwait(false);
        return ReadChat(json);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ChatMember>> ListMembersAsync(ChatTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.IsChannel
            ? ListAsync($"{ChannelPath(target)}/members", string.Empty, ChatScopes.ChannelMemberRead, ReadMember, cancellationToken)
            : ListAsync($"{ChatPath(target)}/members", string.Empty, ChatScopes.ChatRead, ReadMember, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ChatPerson?> GetPersonAsync(string userId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userId);
        RefuseApplication(NoDirectory);
        if (_people.TryGetValue(userId, out var known))
            return known;
        try
        {
            var json = await _graph.GetAsync($"users/{GraphClient.Segment(userId)}?{PersonFields}", ChatScopes.UserRead, cancellationToken).ConfigureAwait(false);
            if (ReadPerson(json) is not { } person)
                return null;
            _people[person.Id] = person;
            return person;
        }
        catch (ChatToolException ex) when (ex.Code == ChatErrorCode.NotFound)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChatPerson>> FindByAddressAsync(string address, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(address);
        RefuseApplication(NoDirectory);
        // The sign-in name first; then the mail, which is how a guest is found by the home address.
        if (await GetPersonAsync(address, cancellationToken).ConfigureAwait(false) is { } person && person.HasAddress(address))
            return [person];

        var filter = Uri.EscapeDataString($"mail eq '{address.Replace("'", "''", StringComparison.Ordinal)}'");
        var json = await _graph.GetAsync($"users?$filter={filter}&{PersonFields}&$top=5", ChatScopes.UserRead, cancellationToken).ConfigureAwait(false);
        // Only people whose own address it is: the mail of a guest can be set to anything by their tenant.
        return GraphJson.Items(json).Select(ReadPerson).OfType<ChatPerson>().Where(found => found.HasAddress(address)).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChatPerson>> FindByNameAsync(string name, int top, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);
        RefuseApplication(NoDirectory);
        var words = name.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        var search = Uri.EscapeDataString($"\"displayName:{words}\"");
        var path = string.Create(CultureInfo.InvariantCulture, $"users?$search={search}&{PersonFields}&$top={top}");
        var json = await _graph.GetAsync(path, ChatScopes.UserRead, cancellationToken, eventual: true).ConfigureAwait(false);
        return GraphJson.Items(json).Select(ReadPerson).OfType<ChatPerson>().ToList();
    }

    /// <inheritdoc />
    public async Task<string?> FindDirectChatAsync(string userId, CancellationToken cancellationToken)
    {
        RefuseApplication("has no one-to-one chat");
        string? page = null;
        for (var read = 0; read < ChatDefaults.MaxChatPages; read++)
        {
            var chats = await ListChatsAsync(ChatDefaults.MaxPage, page, cancellationToken).ConfigureAwait(false);
            var found = chats.Items.FirstOrDefault(chat => chat.IsOneOnOne
                && chat.Members.Any(member => string.Equals(member.UserId, userId, StringComparison.OrdinalIgnoreCase)));
            if (found is not null)
                return found.Id;
            page = chats.Next;
            if (page is null)
                return null;
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<string> OpenDirectChatAsync(string userId, CancellationToken cancellationToken)
    {
        var self = await GetSelfAsync(cancellationToken).ConfigureAwait(false);
        var other = await GetPersonAsync(userId, cancellationToken).ConfigureAwait(false);
        var body = new JsonObject
        {
            ["chatType"] = ChatConversation.OneOnOne,
            ["members"] = new JsonArray(Participant(self.Id, "owner"), Participant(userId, other?.IsGuest == true ? "guest" : "owner")),
        };
        var json = await _graph.PostAsync("chats", body, ChatScopes.ChatCreate, cancellationToken, " The chat may have been opened all the same; no message was posted.").ConfigureAwait(false);
        return GraphJson.Text(json, "id") is { } id && Places.ChatIds.IsConversationId(id) ? id : throw Unexpected("the chat it opened");
    }

    /// <inheritdoc />
    public async Task<ChatPage<ChatMessage>> ReadAsync(ChatTarget target, ChatReadQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(query);
        var top = string.Create(CultureInfo.InvariantCulture, $"$top={query.Top}");
        JsonElement json;
        if (target.IsChannel)
        {
            // A channel's messages can be neither filtered nor ordered; its thread replies no more.
            var path = query.Thread is null ? $"{ChannelPath(target)}/messages" : $"{ChannelPath(target)}/messages/{GraphClient.Segment(query.Thread)}/replies";
            var options = query.Thread is null ? $"{top}&$expand=replies" : top;
            json = await _graph.GetAsync(Address(path, options, query.Page), ChatScopes.ChannelMessageRead, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var options = $"{top}&$orderby={Uri.EscapeDataString("createdDateTime desc")}";
            if (query.Until is { } until)
                options += $"&$filter={Uri.EscapeDataString($"createdDateTime lt {until.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)}")}";
            json = await _graph.GetAsync(Address($"{ChatPath(target)}/messages", options, query.Page), ChatScopes.ChatRead, cancellationToken).ConfigureAwait(false);
        }

        var messages = GraphJson.Items(json).Select(item => GraphMessages.Read(item, target.IsChannel)).ToList();
        return new ChatPage<ChatMessage>(messages, _graph.NextPage(json));
    }

    /// <inheritdoc />
    public async Task<ChatSent> SendAsync(ChatTarget target, ChatOutgoing message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(message);
        RefuseApplication(OnlyReads);
        JsonNode body = GraphMessages.Write(message);
        string path;
        if (target.IsChannel)
        {
            path = message.ReplyTo is null ? $"{ChannelPath(target)}/messages" : $"{ChannelPath(target)}/messages/{GraphClient.Segment(message.ReplyTo)}/replies";
        }
        else if (message.ReplyTo is null)
        {
            path = $"{ChatPath(target)}/messages";
        }
        else
        {
            path = $"{ChatPath(target)}/messages/replyWithQuote";
            body = new JsonObject { ["messageIds"] = new JsonArray(JsonValue.Create(message.ReplyTo)), ["replyMessage"] = body };
        }

        var permission = target.IsChannel ? ChatScopes.ChannelMessageSend : ChatScopes.ChatMessageSend;
        var json = await _graph.PostAsync(path, body, permission, cancellationToken).ConfigureAwait(false);
        return new ChatSent(GraphJson.Text(json, "id"), GraphJson.Date(json, "createdDateTime"));
    }

    /// <inheritdoc />
    public async Task<ChatMessage> GetMessageAsync(ChatTarget target, string? thread, string messageId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        var permission = target.IsChannel ? ChatScopes.ChannelMessageRead : ChatScopes.ChatRead;
        var json = await _graph.GetAsync(MessagePath(target, thread, messageId), permission, cancellationToken).ConfigureAwait(false);
        var message = GraphMessages.Read(json, target.IsChannel);
        return message.Id == messageId ? message : throw Unexpected("the message asked for");
    }

    /// <inheritdoc />
    public async Task<ChatSearchPage> SearchAsync(ChatSearchQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        RefuseApplication("searches nothing: Microsoft Graph searches messages for a signed-in user alone");
        var request = new JsonObject
        {
            ["entityTypes"] = new JsonArray(JsonValue.Create("chatMessage")),
            ["query"] = new JsonObject { ["queryString"] = GraphSearch.QueryString(query) },
            ["from"] = query.Skip,
            ["size"] = query.Top,
        };
        var json = await _graph.QueryAsync("search/query", new JsonObject { ["requests"] = new JsonArray(request) }, ChatScopes.ChatRead, cancellationToken).ConfigureAwait(false);
        return GraphSearch.Read(json);
    }

    /// <inheritdoc />
    public Task EditAsync(ChatTarget target, string? thread, string messageId, ChatOutgoing message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(message);
        RefuseApplication(OnlyReads);
        return _graph.WriteAsync(
            HttpMethod.Patch, MessagePath(target, thread, messageId), GraphMessages.Write(message), WritePermission(target),
            " The message may have been rewritten all the same: read it before trying again.", cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(ChatTarget target, string? thread, string messageId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        RefuseApplication(OnlyReads);
        // A chat's message is deleted under its user, the one form Graph documents.
        var owner = target.IsChannel ? string.Empty : $"users/{GraphClient.Segment((await GetSelfAsync(cancellationToken).ConfigureAwait(false)).Id)}/";
        await _graph.WriteAsync(
            HttpMethod.Post, $"{owner}{MessagePath(target, thread, messageId)}/softDelete", body: null, WritePermission(target),
            " The message may have been deleted all the same: read the place before trying again.", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task ReactAsync(ChatTarget target, string messageId, string reaction, bool remove, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        RefuseApplication(OnlyReads);
        return _graph.WriteAsync(
            HttpMethod.Post,
            $"{MessagePath(target, thread: null, messageId)}/{(remove ? "unsetReaction" : "setReaction")}",
            new JsonObject { ["reactionType"] = reaction },
            ChatScopes.ChatMessageSend,
            " The reaction may have changed all the same: read the message before trying again.",
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ChatMeetingLink?> GetMeetingLinkAsync(string chatId, CancellationToken cancellationToken)
    {
        var json = await _graph.GetAsync($"chats/{GraphClient.Segment(chatId)}", ChatScopes.ChatRead, cancellationToken).ConfigureAwait(false);
        if (GraphJson.Object(json, "onlineMeetingInfo") is not { } meeting || GraphJson.Text(meeting, "joinWebUrl") is not { } link)
            return null;
        return new ChatMeetingLink(link, GraphJson.Object(meeting, "organizer") is { } organizer ? GraphJson.Text(organizer, "id") : null);
    }

    /// <inheritdoc />
    public async Task<ChatMeeting?> FindMeetingAsync(string joinLink, string? organizer, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(joinLink);
        var owner = MeetingOwner(organizer);
        // In a literal of the filter a quote is doubled; the rest is encoded, a percent sign included.
        var filter = Uri.EscapeDataString($"JoinWebUrl eq '{joinLink.Replace("'", "''", StringComparison.Ordinal)}'");
        var json = await _graph.GetAsync($"{MeetingsPath(owner)}?$filter={filter}", ChatScopes.OnlineMeetingsRead, cancellationToken).ConfigureAwait(false);
        var found = GraphJson.Items(json).ToList();
        if (found.Count > 1)
            throw new ChatToolException(ChatErrorCode.NotFound, "Several meetings have this link; none is read.");
        if (found.Count == 0 || GraphJson.Text(found[0], "id") is not { Length: > 0 } id)
            return null;
        var chat = GraphJson.Object(found[0], "chatInfo") is { } info ? GraphJson.Text(info, "threadId") : null;
        return new ChatMeeting(id, GraphJson.Text(found[0], "subject"), GraphJson.Date(found[0], "startDateTime"), chat, owner);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ChatTranscript>> ListTranscriptsAsync(ChatMeeting meeting, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(meeting);
        return ListAsync(
            $"{MeetingPath(meeting)}/transcripts", string.Empty, ChatScopes.TranscriptRead,
            json => GraphJson.Text(json, "id") is { Length: > 0 } id
                ? new ChatTranscript(id, GraphJson.Date(json, "createdDateTime"), GraphJson.Date(json, "endDateTime"))
                : null,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ChatTranscriptText> ReadTranscriptAsync(ChatMeeting meeting, string transcriptId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(meeting);
        var path = $"{MeetingPath(meeting)}/transcripts/{GraphClient.Segment(transcriptId)}/content";
        try
        {
            return new ChatTranscriptText(await _graph.GetTextAsync(path, "text/vtt", ChatScopes.TranscriptRead, cancellationToken).ConfigureAwait(false), HasSpeakers: true);
        }
        catch (ChatToolException ex) when (ex.Code == ChatErrorCode.NotSupportedByAccount && ex.InnerException is null)
        {
            return new ChatTranscriptText(
                await _graph.GetTextAsync(path, "application/vnd.microsoft.graph.transcript+text", ChatScopes.TranscriptRead, cancellationToken).ConfigureAwait(false),
                HasSpeakers: false);
        }
    }

    /// <summary>The user an application reads a meeting under: who organizes it, whom its access policy must hold.</summary>
    private string? MeetingOwner(string? organizer)
    {
        if (!_graph.Account.IsApplication)
            return null;
        return organizer is not null && ChatIds.IsDirectoryId(organizer)
            ? organizer
            : throw new ChatToolException(
                ChatErrorCode.NotSupportedByAccount,
                $"Chat account '{_graph.Account.Name}' is an application, which finds a meeting by its chat alone, when the chat tells who organizes it: pass `place`, the chat of the meeting.");
    }

    private static string MeetingsPath(string? owner) => owner is null ? "me/onlineMeetings" : $"users/{GraphClient.Segment(owner)}/onlineMeetings";

    private static string MeetingPath(ChatMeeting meeting) => $"{MeetingsPath(meeting.Organizer)}/{GraphClient.Segment(meeting.Id)}";

    private static string MessagePath(ChatTarget target, string? thread, string messageId)
    {
        if (!target.IsChannel)
            return $"{ChatPath(target)}/messages/{GraphClient.Segment(messageId)}";
        return thread is null
            ? $"{ChannelPath(target)}/messages/{GraphClient.Segment(messageId)}"
            : $"{ChannelPath(target)}/messages/{GraphClient.Segment(thread)}/replies/{GraphClient.Segment(messageId)}";
    }

    private static string WritePermission(ChatTarget target) => target.IsChannel ? ChatScopes.ChannelMessageReadWrite : ChatScopes.ChatReadWrite;

    /// <summary>
    /// The teams the allowed places name, by id: an application reaches every team of the
    /// tenant, and lists no other. A team once found is kept, so a rename changes nothing until the next start.
    /// </summary>
    private async Task<IReadOnlyList<ChatTeam>> AllowedTeamsAsync(CancellationToken cancellationToken)
    {
        var teams = new List<ChatTeam>();
        foreach (var written in _graph.Account.ReadPlaces
            .Where(place => place.Form is PlaceForm.Team or PlaceForm.Channel)
            .Select(place => place.Value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!_allowedTeams.TryGetValue(written, out var found) && await FindTeamAsync(written, cancellationToken).ConfigureAwait(false) is { } team)
                _allowedTeams[written] = found = team;
            if (found is not null)
                teams.Add(found);
        }

        return teams;
    }

    private async Task<ChatTeam?> FindTeamAsync(string id, CancellationToken cancellationToken)
    {
        try
        {
            var json = await _graph.GetAsync($"teams/{GraphClient.Segment(id)}?$select=id,displayName", ChatScopes.TeamRead, cancellationToken).ConfigureAwait(false);
            return Named(json, (key, name) => new ChatTeam(key, name));
        }
        catch (ChatToolException ex) when (ex.Code is ChatErrorCode.NotFound or ChatErrorCode.InvalidRequest)
        {
            // One wrong entry must not close the others: the check says which.
            return null;
        }
    }

    /// <summary>The chats the allowed places name: no request lists the chats of an application.</summary>
    private async Task<ChatPage<ChatConversation>> AllowedChatsAsync(int top, string? page, CancellationToken cancellationToken)
    {
        var skipped = 0;
        if (page is not null && (!page.StartsWith(Skip, StringComparison.Ordinal) || !int.TryParse(page.AsSpan(Skip.Length), NumberStyles.None, CultureInfo.InvariantCulture, out skipped)))
            throw new ChatToolException(ChatErrorCode.InvalidRequest, "`cursor` is not one this listing handed out.");
        var ids = _graph.Account.ReadPlaces.Where(place => place.Form == PlaceForm.Chat).Select(place => place.Value).Distinct(StringComparer.Ordinal).ToList();
        var chats = new List<ChatConversation>();
        foreach (var id in ids.Skip(skipped).Take(top))
        {
            try
            {
                chats.Add(await GetChatAsync(id, cancellationToken).ConfigureAwait(false));
            }
            catch (ChatToolException ex) when (ex.Code is ChatErrorCode.NotFound or ChatErrorCode.InvalidRequest)
            {
                // Gone, mistyped, or of another tenant: there is nothing to list, and the check says it.
            }
        }

        var next = (long)skipped + top;
        return new ChatPage<ChatConversation>(chats, next < ids.Count ? string.Create(CultureInfo.InvariantCulture, $"{Skip}{next}") : null);
    }

    private const string Skip = "$skip=";

    private void RefuseApplication(string why)
    {
        if (_graph.Account.IsApplication)
            throw new ChatToolException(ChatErrorCode.NotSupportedByAccount, $"Chat account '{_graph.Account.Name}' is an application, which {why}.");
    }

    private static string ChannelPath(ChatTarget target) =>
        $"teams/{GraphClient.Segment(target.TeamId ?? string.Empty)}/channels/{GraphClient.Segment(target.ChannelId ?? string.Empty)}";

    private static string ChatPath(ChatTarget target) => $"chats/{GraphClient.Segment(target.ChatId ?? string.Empty)}";

    private static string Address(string path, string query, string? page)
    {
        var parts = new[] { query, page }.Where(part => !string.IsNullOrEmpty(part));
        var joined = string.Join('&', parts);
        return joined.Length == 0 ? path : $"{path}?{joined}";
    }

    private async Task<IReadOnlyList<T>> ListAsync<T>(
        string path, string query, string permission, Func<JsonElement, T?> read, CancellationToken cancellationToken)
        where T : class
    {
        var items = new List<T>();
        string? page = null;
        for (var count = 0; count < ChatDefaults.MaxListPages; count++)
        {
            var json = await _graph.GetAsync(Address(path, query, page), permission, cancellationToken).ConfigureAwait(false);
            items.AddRange(GraphJson.Items(json).Select(read).OfType<T>());
            page = _graph.NextPage(json);
            if (page is null)
                break;
        }

        return items;
    }

    private JsonObject Participant(string userId, string role) => new()
    {
        ["@odata.type"] = "#microsoft.graph.aadUserConversationMember",
        ["roles"] = new JsonArray(JsonValue.Create(role)),
        ["user@odata.bind"] = new Uri(_graph.Account.ApiBase, $"users('{GraphClient.Segment(userId)}')").AbsoluteUri,
    };

    private static ChatPerson? ReadPerson(JsonElement json) => GraphJson.Text(json, "id") is { Length: > 0 } id
        ? new ChatPerson(
            id,
            GraphJson.Text(json, "displayName") ?? string.Empty,
            GraphJson.Text(json, "userPrincipalName"),
            GraphJson.Text(json, "mail"),
            GraphJson.Text(json, "userType"),
            GraphJson.Text(json, "creationType"))
        : null;

    private static ChatConversation ReadChat(JsonElement json) => new(
        GraphJson.Text(json, "id") ?? string.Empty,
        GraphJson.Text(json, "chatType") ?? string.Empty,
        GraphJson.Text(json, "topic"),
        GraphJson.Items(json, "members").Select(ReadMember).ToList());

    private static ChatMember ReadMember(JsonElement json)
    {
        var roles = json.TryGetProperty("roles", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Where(role => role.ValueKind == JsonValueKind.String).Select(role => role.GetString()).OfType<string>().ToList()
            : [];
        return new ChatMember(GraphJson.Text(json, "userId"), GraphJson.Text(json, "displayName") ?? string.Empty, GraphJson.Text(json, "email"), roles);
    }

    private static T? Named<T>(JsonElement json, Func<string, string, T> create)
        where T : class =>
        GraphJson.Text(json, "id") is { Length: > 0 } id ? create(id, GraphJson.Text(json, "displayName") ?? string.Empty) : null;

    private static ChatToolException Unexpected(string what) =>
        new(ChatErrorCode.ServerError, $"Microsoft Graph did not return {what}.");
}
