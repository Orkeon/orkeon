using Microsoft.Extensions.Logging;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Chat.Accounts;
using Orkeon.Tools.Chat.Configuration;
using Orkeon.Tools.Chat.Dtos;
using Orkeon.Tools.Chat.Security;

namespace Orkeon.Tools.Chat.Tools;

/// <summary>Deletes a message the account sent, in the account's allowed places only.</summary>
[ToolContract("chat_delete",
    Name = "chat_delete",
    Description = "Delete a message the account itself sent, in a place the operator allowed posting in; the service keeps it recoverable. Needs the Delete right and, in a channel, permissions a tenant administrator grants.",
    Category = "Chat")]
internal sealed partial class ChatDeleteTool : ToolBase<ChatDeleteRequest, ChatMessageWriteResponse>
{
    private readonly ChatAccess _access;
    private readonly WriteTurns _turns;
    private readonly ILogger _log;

    /// <summary>Creates the tool.</summary>
    public ChatDeleteTool(ChatAccess access, WriteTurns turns, ILogger<ChatDeleteTool>? logger = null) : base(logger)
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
    protected override string? ValidateTypedRequest(ChatDeleteRequest request) =>
        MessageAddress.Refuse(request?.Place, request?.MessageId, request?.Thread);

    /// <inheritdoc />
    protected override async Task<ChatMessageWriteResponse> ExecuteTypedAsync(ChatDeleteRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var session = _access.Authorize(request.Account, ChatRights.Delete);
        var place = MessageAddress.Place(session.Account, request.Place, request.Thread);
        var target = await MessageWrites.AdmitAsync(session, place, cancellationToken).ConfigureAwait(false);
        await MessageWrites.OwnAsync(session, target, request.Thread, request.MessageId, cancellationToken).ConfigureAwait(false);
        await _turns.TakeAsync(session, target, cancellationToken).ConfigureAwait(false);
        await session.Provider.DeleteAsync(target, request.Thread, request.MessageId, cancellationToken).ConfigureAwait(false);
        LogDeleted(_log, session.Account.Name, request.MessageId, target.Place);
        return new ChatMessageWriteResponse { Account = session.Account.Name, Place = target.Place, MessageId = request.MessageId };
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "chat_delete: account {Account} deleted message {MessageId} in {Place}")]
    private static partial void LogDeleted(ILogger logger, string account, string messageId, string place);
}
