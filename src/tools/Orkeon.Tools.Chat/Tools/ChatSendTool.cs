using System.Globalization;
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
using Orkeon.Tools.Chat.Providers.TeamsWebhook;
using Orkeon.Tools.Chat.Security;
using Orkeon.Tools.Chat.Text;

namespace Orkeon.Tools.Chat.Tools;

/// <summary>Posts a message, in the account's allowed places only.</summary>
[ToolContract("chat_send",
    Name = "chat_send",
    Description = "Post a message in a channel, a chat or the one-to-one chat with a person, or reply to a message (`reply_to`), as the account's user. Only places the operator allowed for the account can be posted in; a TeamsWebhook account posts into its one place and takes no `place`. A file is not uploaded: link to it. Needs the Send right.",
    Category = "Chat")]
internal sealed partial class ChatSendTool : ToolBase<ChatSendRequest, ChatSendResponse>
{
    private const string Markdown = "markdown";
    private const string PlainText = "text";

    private static readonly string[] FormatNames = [Markdown, PlainText];

    private readonly ChatAccess _access;
    private readonly SendQuota _quota;
    private readonly PostPacer _pacer;
    private readonly ILogger _log;

    /// <summary>Creates the tool.</summary>
    public ChatSendTool(ChatAccess access, SendQuota quota, PostPacer pacer, ILogger<ChatSendTool>? logger = null) : base(logger)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(quota);
        ArgumentNullException.ThrowIfNull(pacer);
        _access = access;
        _quota = quota;
        _pacer = pacer;
        _log = (ILogger?)logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
    }

    /// <inheritdoc />
    public override ToolAccess Access => ToolAccess.Execute;

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(ChatSendRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.Text))
            return "`text` is required";
        if (Format(request) is not (Markdown or PlainText))
            return "`format` must be markdown or text";
        if (request.ReplyTo is { } reply && !ChatIds.IsMessageId(reply))
            return "`reply_to` must be the id of a message from chat_read";
        if (request.Mentions is { Count: > ChatDefaults.MaxMentions })
            return string.Create(CultureInfo.InvariantCulture, $"`mentions` holds at most {ChatDefaults.MaxMentions} ids");
        return request.Mentions?.Any(id => id is null || !ChatIds.IsDirectoryId(id)) == true
            ? "`mentions` holds ids of people from chat_members, not names"
            : null;
    }

    /// <inheritdoc />
    protected override async Task<ChatSendResponse> ExecuteTypedAsync(ChatSendRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var session = _access.Authorize(request.Account, ChatRights.Send);
        if (session.Provider is TeamsWebhookProvider webhook)
            return await PostAsync(session.Account, webhook, request, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(request.Place))
            throw new ChatToolException(ChatErrorCode.InvalidRequest, "`place` is required: pass a place from chat_list, or user:<address>");
        var place = ChatAccess.Place(request.Place);
        var html = Render(request);
        var target = await session.Gate.AdmitAsync(place, PlaceUse.Send, cancellationToken).ConfigureAwait(false);
        var mentions = await MentionsAsync(session.Provider, request.Mentions, cancellationToken).ConfigureAwait(false);

        _pacer.RefuseWhenBusy(target.Place);
        ConsumeQuota(session.Account);

        if (target.Person is { } person)
            target = new ChatTarget { ChatId = await session.Provider.OpenDirectChatAsync(person.Id, cancellationToken).ConfigureAwait(false) };

        await _pacer.WaitAsync(target.Place, cancellationToken).ConfigureAwait(false);
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

    /// <summary>Posts through a webhook, whose address is the place.</summary>
    private async Task<ChatSendResponse> PostAsync(
        ResolvedChatAccount account, TeamsWebhookProvider webhook, ChatSendRequest request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.Place))
            throw Refused(account, "`place`", "posts into the one place of its address");
        if (request.ReplyTo is not null)
            throw Refused(account, "`reply_to`", "only starts a message");
        if (request.Mentions is { Count: > 0 })
            throw Refused(account, "`mentions`", "mentions nobody");

        var payload = TeamsWebhookProvider.Payload(request.Text, Format(request) == Markdown);
        var slot = $"webhook:{account.Name}";
        _pacer.RefuseWhenBusy(slot);
        ConsumeQuota(account);
        await _pacer.WaitAsync(slot, cancellationToken).ConfigureAwait(false);
        await webhook.PostAsync(payload, cancellationToken).ConfigureAwait(false);
        LogSent(_log, account.Name, slot, string.Empty);
        return new ChatSendResponse
        {
            Account = account.Name,
            Warning = "The workflow accepted the message before posting it, and returns no id: do not send it again.",
        };
    }

    private static ChatToolException Refused(ResolvedChatAccount account, string argument, string what) => new(
        ChatErrorCode.NotSupportedByAccount,
        $"Chat account '{account.Name}' is a webhook, which {what}: leave {argument} out. Nothing was posted.");

    private void ConsumeQuota(ResolvedChatAccount account)
    {
        if (_quota.TryConsume(account.Name, account.MaxPerHour))
            return;
        throw new ChatToolException(
            ChatErrorCode.Throttled,
            string.Create(CultureInfo.InvariantCulture,
                $"Chat account '{account.Name}' already attempted {account.MaxPerHour} messages in the last hour (Send:MaxPerHour); nothing was posted."));
    }

    private static string Format(ChatSendRequest request) =>
        string.IsNullOrWhiteSpace(request.Format) ? Markdown : FormatNames.FirstOrDefault(known => string.Equals(known, request.Format.Trim(), StringComparison.OrdinalIgnoreCase)) ?? request.Format;

    private static string Render(ChatSendRequest request)
    {
        var text = request.Text;
        // Bounded ahead of the rendering too: every character is a byte of HTML at least.
        var html = text.Length > ChatDefaults.MaxMessageBytes
            ? text
            : Format(request) == PlainText ? MarkdownRenderer.FromText(text) : MarkdownRenderer.FromMarkdown(text);
        if (html.Length == 0)
            throw new ChatToolException(ChatErrorCode.InvalidRequest, "`text` renders to an empty message; nothing was posted.");
        if (Encoding.UTF8.GetByteCount(html) <= ChatDefaults.MaxMessageBytes)
            return html;
        throw new ChatToolException(
            ChatErrorCode.InvalidRequest,
            string.Create(CultureInfo.InvariantCulture, $"The message is over {ChatDefaults.MaxMessageBytes / 1024} KB once rendered; nothing was posted. Shorten it, or split it."));
    }

    private static async Task<IReadOnlyList<ChatPerson>> MentionsAsync(IChatProvider provider, IReadOnlyList<string>? ids, CancellationToken cancellationToken)
    {
        var people = new List<ChatPerson>();
        foreach (var id in (ids ?? []).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            people.Add(await provider.GetPersonAsync(id, cancellationToken).ConfigureAwait(false)
                ?? throw new ChatToolException(ChatErrorCode.NotFound, $"`mentions` holds '{id}', who is not in the directory; nothing was posted."));
        }

        return people;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "chat_send: account {Account} posted message {MessageId} in {Place}")]
    private static partial void LogSent(ILogger logger, string account, string place, string messageId);
}
