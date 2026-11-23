using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Chat.Accounts;
using Orkeon.Tools.Chat.Configuration;
using Orkeon.Tools.Chat.Dtos;
using Orkeon.Tools.Chat.Places;
using Orkeon.Tools.Chat.Security;

namespace Orkeon.Tools.Chat.Tools;

/// <summary>Sets or removes the account's reaction on a message of a chat it may post in.</summary>
[ToolContract("chat_react",
    Name = "chat_react",
    Description = "Set or remove (`remove`) the account's reaction on a message of a chat the operator allowed posting in. A channel's messages take none. Needs the React right.",
    Category = "Chat")]
internal sealed partial class ChatReactTool : ToolBase<ChatReactRequest, ChatReactResponse>
{
    private const int MaxReactionLength = 32;

    private readonly ChatAccess _access;
    private readonly WriteTurns _turns;
    private readonly ILogger _log;

    /// <summary>Creates the tool.</summary>
    public ChatReactTool(ChatAccess access, WriteTurns turns, ILogger<ChatReactTool>? logger = null) : base(logger)
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
    protected override string? ValidateTypedRequest(ChatReactRequest request)
    {
        if (MessageAddress.Refuse(request?.Place, request?.MessageId, thread: null) is { } refusal)
            return refusal;
        return IsEmoji(request?.Reaction) ? null : "`reaction` must be one emoji character, not a name";
    }

    /// <inheritdoc />
    protected override async Task<ChatReactResponse> ExecuteTypedAsync(ChatReactRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var session = _access.Authorize(request.Account, ChatRights.React);
        var place = ChatAccess.Place(request.Place);
        if (place.Form == PlaceForm.Channel)
        {
            throw new ChatToolException(
                ChatErrorCode.NotSupportedByAccount,
                "A reaction in a channel is not supported: it was not tried on a real tenant. A chat's messages take reactions; nothing was changed.");
        }

        var target = await MessageWrites.AdmitAsync(session, place, cancellationToken).ConfigureAwait(false);
        await _turns.TakeAsync(session, target, cancellationToken).ConfigureAwait(false);
        var reaction = request.Reaction.Trim();
        await session.Provider.ReactAsync(target, request.MessageId, reaction, request.Remove == true, cancellationToken).ConfigureAwait(false);
        LogReacted(_log, session.Account.Name, request.Remove == true ? "removed" : "set", request.MessageId, target.Place);
        return new ChatReactResponse
        {
            Account = session.Account.Name,
            Place = target.Place,
            MessageId = request.MessageId,
            Reaction = reaction,
            Removed = request.Remove == true ? true : null,
        };
    }

    /// <summary>Whether <paramref name="text"/> is one character as a reader sees it, and no letter or plain sign.</summary>
    private static bool IsEmoji(string? text)
    {
        var reaction = text?.Trim() ?? string.Empty;
        return reaction.Length is > 0 and <= MaxReactionLength
            && new StringInfo(reaction).LengthInTextElements == 1
            && reaction.EnumerateRunes().Any(IsSymbol)
            && !reaction.EnumerateRunes().Any(rune => Rune.IsLetter(rune) || Rune.IsControl(rune) || Rune.IsWhiteSpace(rune) || rune == Rune.ReplacementChar);
    }

    private static bool IsSymbol(Rune rune) =>
        !rune.IsAscii && (rune.Value is 0x203C or 0x2049
            || Rune.GetUnicodeCategory(rune) is UnicodeCategory.OtherSymbol or UnicodeCategory.MathSymbol or UnicodeCategory.EnclosingMark);

    [LoggerMessage(Level = LogLevel.Information, Message = "chat_react: account {Account} {Action} a reaction on message {MessageId} in {Place}")]
    private static partial void LogReacted(ILogger logger, string account, string action, string messageId, string place);
}
