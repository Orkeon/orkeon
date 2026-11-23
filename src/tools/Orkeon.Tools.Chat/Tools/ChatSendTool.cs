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

/// <summary>Posts a message, in the account's allowed places only.</summary>
[ToolContract("chat_send",
    Name = "chat_send",
    Description = "Post a message in a channel, a chat or the one-to-one chat with a person, or reply to a message (`reply_to`), as the account's user. Only places the operator allowed for the account can be posted in. A file is not uploaded: link to it. Needs the Send right.",
    Category = "Chat")]
internal sealed partial class ChatSendTool : ToolBase<ChatSendRequest, ChatSendResponse>
{
    private readonly ChatAccess _access;
    private readonly WriteTurns _turns;
    private readonly ILogger _log;

    /// <summary>Creates the tool.</summary>
    public ChatSendTool(ChatAccess access, WriteTurns turns, ILogger<ChatSendTool>? logger = null) : base(logger)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(turns);
        _access = access;
        _turns = turns;
        _log = (ILogger?)logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
    }

    /// <inheritdoc />
    public override ToolAccess Access => ToolAccess.Execute;

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(ChatSendRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.Place))
            return "`place` is required: pass a place from chat_list, or user:<address>";
        if (string.IsNullOrWhiteSpace(request.Text))
            return "`text` is required";
        if (!MessageBody.IsFormat(request.Format))
            return MessageBody.FormatRefusal;
        if (request.ReplyTo is { } reply && !ChatIds.IsMessageId(reply))
            return "`reply_to` must be the id of a message from chat_read";
        return MessageBody.RefuseMentions(request.Mentions);
    }

    /// <inheritdoc />
    protected override async Task<ChatSendResponse> ExecuteTypedAsync(ChatSendRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var session = _access.Authorize(request.Account, ChatRights.Send);
        var place = ChatAccess.Place(request.Place);
        var html = MessageBody.Render(request.Text, request.Format);
        var target = await session.Gate.AdmitAsync(place, PlaceUse.Send, cancellationToken).ConfigureAwait(false);
        var mentions = await MessageBody.MentionsAsync(session.Provider, request.Mentions, cancellationToken).ConfigureAwait(false);

        _turns.Take(session, target);
        if (target.Person is { } person)
            target = new ChatTarget { ChatId = await session.Provider.OpenDirectChatAsync(person.Id, cancellationToken).ConfigureAwait(false) };

        await _turns.WaitAsync(target, cancellationToken).ConfigureAwait(false);
        var sent = await session.Provider.SendAsync(target, new ChatOutgoing(html, mentions, request.ReplyTo), cancellationToken).ConfigureAwait(false);
        LogSent(_log, session.Account.Name, target.Place, sent.Id ?? string.Empty);
        return new ChatSendResponse
        {
            Account = session.Account.Name,
            Place = target.Place,
            MessageId = sent.Id,
            SentAt = ChatToolHelpers.Iso(sent.Created),
            Warning = sent.Id is null ? "The message was posted, but the service's answer held no id: do not send it again." : null,
        };
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "chat_send: account {Account} posted message {MessageId} in {Place}")]
    private static partial void LogSent(ILogger logger, string account, string place, string messageId);
}
