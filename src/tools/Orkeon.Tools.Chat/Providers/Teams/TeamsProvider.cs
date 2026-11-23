using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Orkeon.Tools.Chat.Configuration;
using Orkeon.Tools.Chat.Constants;

namespace Orkeon.Tools.Chat.Providers.Teams;

/// <summary>Microsoft Teams through Microsoft Graph v1.0, as the signed-in user.</summary>
internal sealed class TeamsProvider : IChatProvider
{
    private const string PersonFields = "$select=id,displayName,userPrincipalName,mail";

    private readonly GraphClient _graph;
    private readonly ConcurrentDictionary<string, ChatPerson> _people = new(StringComparer.OrdinalIgnoreCase);
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
        var json = await _graph.GetAsync($"me?{PersonFields}", ChatScopes.UserRead, cancellationToken).ConfigureAwait(false);
        var self = ReadPerson(json) ?? throw Unexpected("the signed-in user");
        _self = self;
        return self;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ChatTeam>> ListTeamsAsync(CancellationToken cancellationToken) =>
        ListAsync("me/joinedTeams", string.Empty, ChatScopes.TeamRead, team => Named(team, (id, name) => new ChatTeam(id, name)), cancellationToken);

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
        var words = name.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        var search = Uri.EscapeDataString($"\"displayName:{words}\"");
        var path = string.Create(CultureInfo.InvariantCulture, $"users?$search={search}&{PersonFields}&$top={top}");
        var json = await _graph.GetAsync(path, ChatScopes.UserRead, cancellationToken, eventual: true).ConfigureAwait(false);
        return GraphJson.Items(json).Select(ReadPerson).OfType<ChatPerson>().ToList();
    }

    /// <inheritdoc />
    public async Task<string?> FindDirectChatAsync(string userId, CancellationToken cancellationToken)
    {
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
        return _graph.WriteAsync(
            HttpMethod.Patch, MessagePath(target, thread, messageId), GraphMessages.Write(message), WritePermission(target),
            " The message may have been rewritten all the same: read it before trying again.", cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(ChatTarget target, string? thread, string messageId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
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
        return _graph.WriteAsync(
            HttpMethod.Post,
            $"{MessagePath(target, thread: null, messageId)}/{(remove ? "unsetReaction" : "setReaction")}",
            new JsonObject { ["reactionType"] = reaction },
            ChatScopes.ChatMessageSend,
            " The reaction may have changed all the same: read the message before trying again.",
            cancellationToken);
    }

    private static string MessagePath(ChatTarget target, string? thread, string messageId)
    {
        if (!target.IsChannel)
            return $"{ChatPath(target)}/messages/{GraphClient.Segment(messageId)}";
        return thread is null
            ? $"{ChannelPath(target)}/messages/{GraphClient.Segment(messageId)}"
            : $"{ChannelPath(target)}/messages/{GraphClient.Segment(thread)}/replies/{GraphClient.Segment(messageId)}";
    }

    private static string WritePermission(ChatTarget target) => target.IsChannel ? ChatScopes.ChannelMessageReadWrite : ChatScopes.ChatReadWrite;

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
        ? new ChatPerson(id, GraphJson.Text(json, "displayName") ?? string.Empty, GraphJson.Text(json, "userPrincipalName"), GraphJson.Text(json, "mail"))
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
