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
    private const int MaxListed = 10;
    private const string RejectedReason = "the prompt-injection screen rejected it; an operator can lift Orkeon:Tools:Chat:Screening:WithholdRejected";
    private const string PolicyReason = "the service marks it as a policy violation";
    private const string DeletedReason = "it was deleted";

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
        var scope = request.MessageId is null ? target.Place : $"{target.Place}#{request.MessageId}";
        var cursor = ChatCursor.Decode(request.Cursor, scope, limit);
        return await CollectAsync(session, target, request, cursor, limit, since, until, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ChatReadResponse> CollectAsync(
        ChatSession session, ChatTarget target, ChatReadRequest request, ChatCursor cursor, int limit,
        DateTimeOffset? since, DateTimeOffset? until, CancellationToken cancellationToken)
    {
        var budget = ChatToolHelpers.ResultBudget(Name);
        var textChars = Math.Min(request.TextChars ?? ChatDefaults.DefaultTextChars, Math.Max(ChatDefaults.MaxTextChars, budget - RoomForOneMessage));
        var authors = new AuthorLookup(session.Provider);
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

                var dto = await DescribeAsync(message, textChars, authors, cancellationToken).ConfigureAwait(false);
                var here = next with { Offset = i };
                length += ChatToolHelpers.RenderedLength(dto) + 1;
                // The cursor is in the result too.
                if (messages.Count == limit || (messages.Count > 0 && length + here.Encode().Length > budget))
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

    private static ChatReadResponse Finish(ChatReadResponse envelope, List<ChatMessageDto> messages, ChatCursor? next) => envelope with
    {
        Count = messages.Count,
        NextCursor = next?.Encode(),
        Messages = messages,
    };

    private async Task<ChatMessageDto> DescribeAsync(ChatMessage message, int textChars, AuthorLookup authors, CancellationToken cancellationToken)
    {
        var person = await PersonAsync(message.Author, authors, cancellationToken).ConfigureAwait(false);
        var screening = _screen.Screen(
            [message.Text, message.Author?.Name, person?.Address, .. message.Mentions.Select(mention => mention.Name), .. message.Attachments, .. message.Reactions.Keys]);
        var withheld = message switch
        {
            { IsDeleted: true } => DeletedReason,
            { ViolatesPolicy: true } => PolicyReason,
            _ => screening.Withhold ? RejectedReason : null,
        };
        var dto = new ChatMessageDto
        {
            Id = message.Id,
            Author = Author(message.Author, person),
            Time = ChatToolHelpers.Iso(message.Created),
            Replies = message.Replies,
            MoreReplies = message.MoreReplies ? true : null,
        };
        if (withheld is not null)
        {
            // Nothing of the text shows, its verdict included: the author alone is still there to read.
            return dto with
            {
                Withheld = true,
                Reason = withheld,
                Screening = withheld == RejectedReason ? null : _screen.Flag(message.Author?.Name, person?.Address),
            };
        }

        return dto with
        {
            Edited = ChatToolHelpers.Iso(message.Edited),
            Text = ChatToolHelpers.Head(message.Text, textChars),
            TextTruncated = message.Text.Length > textChars ? true : null,
            Screening = screening.Verdict == "clean" ? null : screening.Verdict,
            HiddenText = message.HadHiddenText ? true : null,
            Mentions = message.Mentions.Count == 0 ? null : message.Mentions.Take(MaxListed)
                .Select(mention => new ChatMentionDto { Id = mention.Id, Name = _screen.Label(mention.Name) }).ToList(),
            Reactions = message.Reactions.Count == 0 ? null : message.Reactions.Take(MaxListed)
                .GroupBy(reaction => _screen.Label(reaction.Key) ?? "?", StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Sum(reaction => reaction.Value), StringComparer.Ordinal),
            Attachments = message.Attachments.Count == 0 ? null : message.Attachments.Take(MaxListed).Select(name => _screen.Label(name)).OfType<string>().ToList(),
        };
    }

    private static async Task<ChatPerson?> PersonAsync(ChatAuthor? author, AuthorLookup authors, CancellationToken cancellationToken) =>
        author is null or { IsApplication: true } or { IsExternal: true } ? null : await authors.FindAsync(author.Id, cancellationToken).ConfigureAwait(false);

    private ChatAuthorDto? Author(ChatAuthor? author, ChatPerson? person)
    {
        if (author is null)
            return null;
        return new ChatAuthorDto
        {
            Id = author.Id,
            Name = _screen.Label(author.Name),
            Address = _screen.Label(person?.Address),
            Guest = author.IsExternal || person?.IsGuest == true ? true : null,
            Application = author.IsApplication ? true : null,
        };
    }

    /// <summary>The directory's record of the authors of one call, a bounded number of them.</summary>
    private sealed class AuthorLookup(IChatProvider provider)
    {
        private readonly Dictionary<string, ChatPerson?> _known = new(StringComparer.OrdinalIgnoreCase);

        public async Task<ChatPerson?> FindAsync(string? id, CancellationToken cancellationToken)
        {
            if (id is null || !ChatIds.IsDirectoryId(id))
                return null;
            if (_known.TryGetValue(id, out var known))
                return known;
            if (_known.Count >= ChatDefaults.MaxAuthorLookups)
                return null;
            ChatPerson? person = null;
            try
            {
                person = await provider.GetPersonAsync(id, cancellationToken).ConfigureAwait(false);
            }
            catch (ChatToolException)
            {
                // The message is worth returning without its author's address.
            }

            _known[id] = person;
            return person;
        }
    }
}
