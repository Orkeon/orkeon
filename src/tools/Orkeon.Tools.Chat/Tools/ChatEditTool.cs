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

/// <summary>Rewrites a message the account sent, in the account's allowed places only.</summary>
[ToolContract("chat_edit",
    Name = "chat_edit",
    Description = "Rewrite a message the account itself sent, in a place the operator allowed posting in. The whole text and its mentions are replaced. Needs the Edit right and, in a channel, permissions a tenant administrator grants.",
    Category = "Chat")]
internal sealed partial class ChatEditTool : ToolBase<ChatEditRequest, ChatMessageWriteResponse>
{
    private readonly ChatAccess _access;
    private readonly WriteTurns _turns;
    private readonly ILogger _log;

    /// <summary>Creates the tool.</summary>
    public ChatEditTool(ChatAccess access, WriteTurns turns, ILogger<ChatEditTool>? logger = null) : base(logger)
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
    protected override string? ValidateTypedRequest(ChatEditRequest request)
    {
        if (MessageAddress.Refuse(request?.Place, request?.MessageId, request?.Thread) is { } refusal)
            return refusal;
        if (string.IsNullOrWhiteSpace(request!.Text))
            return "`text` is required";
        return MessageBody.IsFormat(request.Format) ? MessageBody.RefuseMentions(request.Mentions) : MessageBody.FormatRefusal;
    }

    /// <inheritdoc />
    protected override async Task<ChatMessageWriteResponse> ExecuteTypedAsync(ChatEditRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var session = _access.Authorize(request.Account, ChatRights.Edit);
        var place = MessageAddress.Place(session.Account, request.Place, request.Thread);
        if (session.Account.Cloud != ChatCloud.Global)
            throw new ChatToolException(ChatErrorCode.NotSupportedByAccount, "Microsoft Graph edits messages in the worldwide cloud alone; nothing was changed.");

        var html = MessageBody.Render(request.Text, request.Format);
        var target = await MessageWrites.AdmitAsync(session, place, cancellationToken).ConfigureAwait(false);
        var message = await MessageWrites.OwnAsync(session, target, request.Thread, request.MessageId, cancellationToken).ConfigureAwait(false);
        if (message.Attachments.Count > 0 || message.HasImages)
        {
            throw new ChatToolException(
                ChatErrorCode.NotSupportedByAccount,
                $"Message '{request.MessageId}' quotes a message or carries attachments or images, which a rewrite would drop; nothing was changed. Post another message instead.");
        }

        var mentions = await MessageBody.MentionsAsync(session.Provider, request.Mentions, cancellationToken).ConfigureAwait(false);
        _turns.Take(session, target);
        await _turns.WaitAsync(target, cancellationToken).ConfigureAwait(false);
        await session.Provider.EditAsync(target, request.Thread, request.MessageId, new ChatOutgoing(html, mentions, ReplyTo: null), cancellationToken).ConfigureAwait(false);
        LogEdited(_log, session.Account.Name, request.MessageId, target.Place);
        return new ChatMessageWriteResponse { Account = session.Account.Name, Place = target.Place, MessageId = request.MessageId };
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "chat_edit: account {Account} rewrote message {MessageId} in {Place}")]
    private static partial void LogEdited(ILogger logger, string account, string messageId, string place);
}
