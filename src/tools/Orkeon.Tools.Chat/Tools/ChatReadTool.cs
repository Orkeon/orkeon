using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Chat.Accounts;
using Orkeon.Tools.Chat.Configuration;
using Orkeon.Tools.Chat.Constants;
using Orkeon.Tools.Chat.Dtos;
using Orkeon.Tools.Chat.Places;
using Orkeon.Tools.Chat.Providers;
using Orkeon.Tools.Chat.Security;

namespace Orkeon.Tools.Chat.Tools;

/// <summary>Reads the messages of a place, screened, a page at a time.</summary>
[ToolContract("chat_read",
    Name = "chat_read",
    Description = "Read the messages of a channel or a chat, or the replies of one message of a channel (`message_id`), newest first, by page and by date, screened for prompt injection. Needs the Read right, a place the operator allowed and, in a channel, a permission a tenant administrator grants.",
    Category = "Chat")]
internal sealed class ChatReadTool : ToolBase<ChatReadRequest, ChatReadResponse>
{
    private const int RoomForOneMessage = 1000;

    private readonly ChatAccess _access;
    private readonly ChatContentScreen _screen;

    /// <summary>Creates the tool.</summary>
    public ChatReadTool(ChatAccess access, ChatContentScreen screen, ILogger<ChatReadTool>? logger = null) : base(logger)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(screen);
        _access = access;
        _screen = screen;
    }

    /// <inheritdoc />
    public override ToolAccess Access => ToolAccess.Read;

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(ChatReadRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.Place))
            return "`place` is required: pass a place from chat_list";
        if (request.MessageId is { } id && !ChatIds.IsMessageId(id))
            return "`message_id` must be the id of a message from chat_read";
        return request.TextChars is <= 0 ? "`text_chars` must be positive" : null;
    }

    /// <inheritdoc />
    protected override async Task<ChatReadResponse> ExecuteTypedAsync(ChatReadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var since = ChatToolHelpers.ParseDate(request.Since, "since");
        var until = ChatToolHelpers.ParseDate(request.Until, "until");
        var session = _access.Authorize(request.Account, ChatRights.Read);
        var place = ChatAccess.Place(request.Place);
        if (place.Form == PlaceForm.Channel)
            ChatAccess.DemandChannelMessages(session.Account);
        else if (request.MessageId is not null)
            throw new ChatToolException(ChatErrorCode.NotSupportedByAccount, "`message_id` reads the replies of a channel's message: the messages of a chat have none, a reply quotes the message it answers.");

        var target = await session.Gate.AdmitAsync(place, PlaceUse.Read, cancellationToken).ConfigureAwait(false);
        if (target.Person is { } person)
        {
            target = new ChatTarget
            {
                ChatId = await session.Provider.FindDirectChatAsync(person.Id, cancellationToken).ConfigureAwait(false)
                    ?? throw new ChatToolException(ChatErrorCode.NotFound, $"No one-to-one chat with '{place.Value}' was found; chat_send opens one."),
            };
        }

        var limit = ChatToolHelpers.Limit(request.Limit);
        var cursor = ChatCursor.Decode(request.Cursor, Scope(target, request), limit);
        return await CollectAsync(session, target, request, cursor, limit, since, until, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ChatReadResponse> CollectAsync(
        ChatSession session, ChatTarget target, ChatReadRequest request, ChatCursor cursor, int limit,
        DateTimeOffset? since, DateTimeOffset? until, CancellationToken cancellationToken)
    {
        var budget = ChatToolHelpers.ResultBudget(Name);
        var textChars = Math.Min(request.TextChars ?? ChatDefaults.DefaultTextChars, Math.Max(ChatDefaults.MaxTextChars, budget - RoomForOneMessage));
        var describer = new ChatMessageDescriber(_screen, session.Provider);
        var messages = new List<ChatMessageDto>();
        var envelope = new ChatReadResponse
        {
            Account = session.Account.Name,
            Place = target.Place,
            Thread = request.MessageId,
            Notice = ChatContentScreen.UntrustedNotice,
        };
        var length = ChatToolHelpers.RenderedLength(envelope);
        var next = cursor;
        for (var read = 0; read < ChatDefaults.MaxPagesPerRead; read++)
        {
            var page = await session.Provider.ReadAsync(
                target, new ChatReadQuery(next.Top, next.Page, until, request.MessageId), cancellationToken).ConfigureAwait(false);
            for (var i = next.Offset; i < page.Items.Count; i++)
            {
                var message = page.Items[i];
                // A chat is read newest first: nothing after this one is inside the dates.
                if (!target.IsChannel && message.Created < since)
                    return Finish(envelope, messages, null);
                if (!message.IsMessage || message.Id.Length == 0 || message.Created < since || message.Created >= until)
                    continue;

                var dto = await describer.DescribeAsync(message, textChars, cancellationToken).ConfigureAwait(false);
                var here = next with { Offset = i };
                length += ChatToolHelpers.RenderedLength(dto) + 1;
                // The cursor is in the result too: the one of this message, or the next page's, which may be the longer.
                var room = here.Encode().Length;
                if (page.Next is not null)
                    room = Math.Max(room, new ChatCursor(next.Place, next.Top, page.Next, 0).Encode().Length);
                if (messages.Count == limit || (messages.Count > 0 && length + room > budget))
                    return Finish(envelope, messages, here);
                messages.Add(dto);
            }

            if (page.Next is null)
                return Finish(envelope, messages, null);
            next = new ChatCursor(next.Place, next.Top, page.Next, 0);
            if (messages.Count == limit)
                return Finish(envelope, messages, next);
        }

        return Finish(envelope, messages, next);
    }

    /// <summary>What a cursor is good for: the place, the thread, and the dates, outside of which a page would be read as another one.</summary>
    private static string Scope(ChatTarget target, ChatReadRequest request)
    {
        var scope = request.MessageId is null ? target.Place : $"{target.Place}#{request.MessageId}";
        var since = request.Since?.Trim();
        var until = request.Until?.Trim();
        return string.IsNullOrEmpty(since) && string.IsNullOrEmpty(until)
            ? scope
            : $"{scope}?{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{since}\n{until}")))[..16]}";
    }

    private static ChatReadResponse Finish(ChatReadResponse envelope, List<ChatMessageDto> messages, ChatCursor? next) => envelope with
    {
        Count = messages.Count,
        NextCursor = next?.Encode(),
        Messages = messages,
    };
}
