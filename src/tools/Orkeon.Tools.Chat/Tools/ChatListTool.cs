using Microsoft.Extensions.Logging;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Chat.Accounts;
using Orkeon.Tools.Chat.Configuration;
using Orkeon.Tools.Chat.Dtos;
using Orkeon.Tools.Chat.Places;
using Orkeon.Tools.Chat.Providers;
using Orkeon.Tools.Chat.Security;

namespace Orkeon.Tools.Chat.Tools;

/// <summary>Lists the places an account may use.</summary>
[ToolContract("chat_list",
    Name = "chat_list",
    Description = "List the places of a chat account that the operator allowed: its teams, the channels of a team, or its chats (one-to-one, group, meeting), with the `place` to pass to chat_read and chat_send. Chats need the Read right; teams and channels the Read or the Send right.",
    Category = "Chat")]
internal sealed class ChatListTool : ToolBase<ChatListRequest, ChatListResponse>
{
    private const string Teams = "teams";
    private const string Channels = "channels";
    private const string Chats = "chats";
    private static readonly string[] Kinds = [Teams, Channels, Chats];

    private const int MaxNames = 8;

    private readonly ChatAccess _access;
    private readonly ChatContentScreen _screen;

    /// <summary>Creates the tool.</summary>
    public ChatListTool(ChatAccess access, ChatContentScreen screen, ILogger<ChatListTool>? logger = null) : base(logger)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(screen);
        _access = access;
        _screen = screen;
    }

    /// <inheritdoc />
    public override ToolAccess Access => ToolAccess.Read;

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(ChatListRequest request)
    {
        var kind = Kind(request);
        if (kind is not (Teams or Channels or Chats))
            return "`kind` must be teams, channels or chats";
        if (kind == Channels && string.IsNullOrWhiteSpace(request?.Team))
            return "`team` is required to list channels: pass a team from chat_list";
        return kind != Channels && !string.IsNullOrWhiteSpace(request?.Team) ? "`team` goes with `kind` channels alone" : null;
    }

    /// <inheritdoc />
    protected override async Task<ChatListResponse> ExecuteTypedAsync(ChatListRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var kind = Kind(request);
        var session = kind == Chats
            ? _access.Authorize(request.Account, ChatRights.Read)
            : _access.Authorize(request.Account, ChatRights.Read, ChatRights.Send);
        var read = session.Account.Grants(ChatRights.Read) ? await session.Gate.RulesAsync(PlaceUse.Read, cancellationToken).ConfigureAwait(false) : new PlaceRules();
        var send = session.Account.Grants(ChatRights.Send) ? await session.Gate.RulesAsync(PlaceUse.Send, cancellationToken).ConfigureAwait(false) : new PlaceRules();

        var limit = ChatToolHelpers.Limit(request.Limit);
        var scope = kind == Channels ? $"{kind}:{request.Team!.Trim()}" : kind;
        var cursor = ChatCursor.Decode(request.Cursor, scope, limit);
        string? next = null;
        IReadOnlyList<ChatPlaceDto> all;
        if (kind == Chats)
        {
            var page = await session.Provider.ListChatsAsync(cursor.Top, cursor.Page, cancellationToken).ConfigureAwait(false);
            next = page.Next;
            all = await ChatPlacesAsync(session, page.Items, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            all = kind == Teams
                ? await TeamPlacesAsync(session, read, send, cancellationToken).ConfigureAwait(false)
                : await ChannelPlacesAsync(session, request.Team!.Trim(), read, send, cancellationToken).ConfigureAwait(false);
        }

        var rest = all.Skip(cursor.Offset).Take(limit).ToList();
        var aside = read.Problems.Concat(send.Problems).Distinct(StringComparer.Ordinal).ToList();
        var envelope = new ChatListResponse
        {
            Account = session.Account.Name,
            Notice = ChatContentScreen.UntrustedNotice,
            NextCursor = (cursor with { Page = next ?? cursor.Page, Offset = all.Count }).Encode(),
            SetAside = aside.Count == 0 ? null : aside,
        };
        var kept = rest.Take(ChatToolHelpers.Fit(rest, envelope, ChatToolHelpers.ResultBudget(Name))).ToList();
        var shown = cursor.Offset + kept.Count;
        return envelope with
        {
            Count = kept.Count,
            Places = kept,
            NextCursor = shown < all.Count
                ? (cursor with { Offset = shown }).Encode()
                : next is null ? null : new ChatCursor(scope, cursor.Top, next, 0).Encode(),
        };
    }

    private static string Kind(ChatListRequest? request)
    {
        var kind = request?.Kind?.Trim();
        if (!string.IsNullOrEmpty(kind))
            return Kinds.FirstOrDefault(known => string.Equals(known, kind, StringComparison.OrdinalIgnoreCase)) ?? kind;
        return string.IsNullOrWhiteSpace(request?.Team) ? Teams : Channels;
    }

    private async Task<IReadOnlyList<ChatPlaceDto>> TeamPlacesAsync(
        ChatSession session, PlaceRules read, PlaceRules send, CancellationToken cancellationToken)
    {
        var teams = await session.Provider.ListTeamsAsync(cancellationToken).ConfigureAwait(false);
        return teams
            .Where(team => read.ShowsTeam(team.Id) || send.ShowsTeam(team.Id))
            .Select(team => Place($"team:{team.Id}", team.Id, "team", team.Name, read.ShowsTeam(team.Id), send.ShowsTeam(team.Id)))
            .ToList();
    }

    private async Task<IReadOnlyList<ChatPlaceDto>> ChannelPlacesAsync(
        ChatSession session, string team, PlaceRules read, PlaceRules send, CancellationToken cancellationToken)
    {
        var id = await session.Gate.TeamIdAsync(team, cancellationToken).ConfigureAwait(false);
        if (!read.ShowsTeam(id) && !send.ShowsTeam(id))
            throw new ChatToolException(ChatErrorCode.PlaceNotAllowed, $"No channel of team '{team}' is among the allowed places of chat account '{session.Account.Name}'.");
        var channels = await session.Provider.ListChannelsAsync(id, cancellationToken).ConfigureAwait(false);
        return channels
            .Where(channel => read.AllowsChannel(id, channel.Id) || send.AllowsChannel(id, channel.Id))
            .Select(channel => Place(
                $"team:{id}/channel:{channel.Id}", channel.Id, "channel", channel.Name, read.AllowsChannel(id, channel.Id), send.AllowsChannel(id, channel.Id)))
            .ToList();
    }

    private async Task<IReadOnlyList<ChatPlaceDto>> ChatPlacesAsync(
        ChatSession session, IReadOnlyList<ChatConversation> chats, CancellationToken cancellationToken)
    {
        // An application is a member of nothing: every name is someone else's.
        var self = session.Account.IsApplication ? null : await session.Provider.GetSelfAsync(cancellationToken).ConfigureAwait(false);
        var places = new List<ChatPlaceDto>();
        foreach (var chat in chats)
        {
            var canRead = await session.Gate.AllowsChatAsync(chat, PlaceUse.Read, cancellationToken).ConfigureAwait(false);
            var canSend = session.Account.Grants(ChatRights.Send)
                && await session.Gate.AllowsChatAsync(chat, PlaceUse.Send, cancellationToken).ConfigureAwait(false);
            if (!canRead && !canSend)
                continue;
            var names = chat.Members
                .Where(member => self is null || !string.Equals(member.UserId, self.Id, StringComparison.OrdinalIgnoreCase))
                .Select(member => member.Name)
                .Take(MaxNames)
                .ToList();
            places.Add(Place($"chat:{chat.Id}", chat.Id, chat.Kind, chat.Topic, canRead, canSend) with
            {
                Members = names.Select(name => _screen.Label(name)).OfType<string>().ToList(),
                Screening = _screen.Flag([chat.Topic, .. names]),
            });
        }

        return places;
    }

    private ChatPlaceDto Place(string place, string id, string kind, string? name, bool canRead, bool canSend) => new()
    {
        Place = place,
        Id = id,
        Kind = kind,
        Name = _screen.Label(name),
        CanRead = canRead,
        CanSend = canSend,
        Screening = _screen.Flag(name),
    };
}
