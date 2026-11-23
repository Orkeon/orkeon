using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Orkeon.Tools.Chat.Auth;
using Orkeon.Tools.Chat.Configuration;
using Orkeon.Tools.Chat.Constants;

namespace Orkeon.Tools.Chat.Providers.TeamsWebhook;

/// <summary>
/// A Workflows webhook: posts into the one place its address was made for, and does nothing else.
/// The address is the secret: it is read for each post, and no message, log line or exception of this
/// class carries it — nor what the transport says of a failure, which may quote it. A post is sent once.
/// </summary>
internal sealed class TeamsWebhookProvider : IChatProvider
{
    private const string MayHaveBeenPosted = " The message may have been posted all the same: look at the place before sending it again.";

    // The payload is sent as JSON, never inside HTML: text outside ASCII keeps its size.
    private static readonly JsonSerializerOptions Compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly Func<HttpClient> _http;
    private readonly ResolvedChatAccount _account;
    private readonly ChatCredentialProvider _credentials;
    private readonly TimeProvider _time;

    /// <summary>Creates the provider of <paramref name="account"/>.</summary>
    public TeamsWebhookProvider(Func<HttpClient> http, ResolvedChatAccount account, ChatCredentialProvider credentials, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(time);
        _http = http;
        _account = account;
        _credentials = credentials;
        _time = time;
    }

    /// <summary>
    /// The payload of a post, refused over 28 KB. The workflow takes an adaptive card: a text block for
    /// Markdown, a rich text block — which formats nothing — for text shown as written.
    /// </summary>
    public static byte[] Payload(string text, bool markdown)
    {
        ArgumentNullException.ThrowIfNull(text);
        JsonObject block = markdown
            ? new() { ["type"] = "TextBlock", ["text"] = text, ["wrap"] = true }
            : new() { ["type"] = "RichTextBlock", ["inlines"] = new JsonArray(new JsonObject { ["type"] = "TextRun", ["text"] = text }) };
        var payload = new JsonObject
        {
            ["type"] = "message",
            ["attachments"] = new JsonArray(new JsonObject
            {
                ["contentType"] = "application/vnd.microsoft.card.adaptive",
                ["contentUrl"] = null,
                ["content"] = new JsonObject
                {
                    ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
                    ["type"] = "AdaptiveCard",
                    ["version"] = "1.2",
                    ["body"] = new JsonArray(block),
                },
            }),
        };

        var bytes = Encoding.UTF8.GetBytes(payload.ToJsonString(Compact));
        return bytes.Length <= ChatDefaults.MaxWebhookBytes
            ? bytes
            : throw new ChatToolException(
                ChatErrorCode.InvalidRequest,
                string.Create(CultureInfo.InvariantCulture, $"The message is over {ChatDefaults.MaxWebhookBytes / 1024} KB as it is sent; nothing was posted. Shorten it, or split it."));
    }

    /// <summary>Posts <paramref name="payload"/>, once.</summary>
    public async Task PostAsync(byte[] payload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, _credentials.WebhookAddress(_account));
        request.Content = new ByteArrayContent(payload);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json", "utf-8");

        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        // The answer's body is never read: an error page may quote the address.
        if (!response.IsSuccessStatusCode)
            throw Describe(response);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpRequestError error;
        try
        {
            return await _http().SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            error = ex.HttpRequestError;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ChatToolException(ChatErrorCode.ServerError, $"The webhook of chat account '{_account.Name}' did not answer in time.{MayHaveBeenPosted}");
        }

        // Only these failures come before a byte of the request has left.
        var unsent = error is HttpRequestError.NameResolutionError or HttpRequestError.ConnectionError or HttpRequestError.SecureConnectionError;
        throw new ChatToolException(
            ChatErrorCode.ServerError,
            $"The webhook of chat account '{_account.Name}' could not be reached ({error}).{(unsent ? " Nothing was posted." : MayHaveBeenPosted)}");
    }

    private ChatToolException Describe(HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;
        var answered = string.Create(CultureInfo.InvariantCulture, $"The webhook of chat account '{_account.Name}' answered HTTP {status}");
        return status switch
        {
            429 => new ChatToolException(ChatErrorCode.Throttled, $"{answered}: it limits the rate of posts.{RetryIn(response)} Nothing was posted."),
            400 or 413 => new ChatToolException(ChatErrorCode.InvalidRequest, $"{answered}: the workflow refused the message. Nothing was posted."),
            408 or >= 500 => new ChatToolException(ChatErrorCode.ServerError, $"{answered}.{MayHaveBeenPosted}"),
            >= 300 and < 400 => new ChatToolException(
                ChatErrorCode.InvalidConfiguration,
                $"{answered}, a redirect, which is never followed. An operator checks the address in the environment variable {_account.WebhookUrlEnvVar}."),
            _ => new ChatToolException(
                ChatErrorCode.InvalidConfiguration,
                $"{answered}: the workflow is gone, stopped, or lets only signed-in callers trigger it. An operator checks it, and the address in the environment variable {_account.WebhookUrlEnvVar}."),
        };
    }

    private string RetryIn(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        var pause = header?.Delta ?? (header?.Date - _time.GetUtcNow());
        return pause > TimeSpan.Zero
            ? string.Create(CultureInfo.InvariantCulture, $" Retry in {Math.Ceiling(pause.Value.TotalSeconds)} s.")
            : string.Empty;
    }

    private ChatToolException NotSupported(string what) => new(
        ChatErrorCode.NotSupportedByAccount,
        $"Chat account '{_account.Name}' is a webhook, which posts into the one place of its address (chat_send): it cannot {what}.");

    /// <inheritdoc />
    public Task<ChatPerson> GetSelfAsync(CancellationToken cancellationToken) => throw NotSupported("act as a user");

    /// <summary>Throws when the variable holds no usable address, before a post is counted.</summary>
    public void RequireAddress() => _ = _credentials.WebhookAddress(_account);

    /// <inheritdoc />
    public Task<IReadOnlyList<ChatTeam>> ListTeamsAsync(CancellationToken cancellationToken) => throw NotSupported("list places");

    /// <inheritdoc />
    public Task<IReadOnlyList<ChatChannel>> ListChannelsAsync(string teamId, CancellationToken cancellationToken) => throw NotSupported("list places");

    /// <inheritdoc />
    public Task<ChatPage<ChatConversation>> ListChatsAsync(int top, string? page, CancellationToken cancellationToken) => throw NotSupported("list places");

    /// <inheritdoc />
    public Task<ChatConversation> GetChatAsync(string chatId, CancellationToken cancellationToken) => throw NotSupported("read a place");

    /// <inheritdoc />
    public Task<IReadOnlyList<ChatMember>> ListMembersAsync(ChatTarget target, CancellationToken cancellationToken) => throw NotSupported("list members");

    /// <inheritdoc />
    public Task<ChatPerson?> GetPersonAsync(string userId, CancellationToken cancellationToken) => throw NotSupported("look a person up");

    /// <inheritdoc />
    public Task<IReadOnlyList<ChatPerson>> FindByAddressAsync(string address, CancellationToken cancellationToken) => throw NotSupported("look a person up");

    /// <inheritdoc />
    public Task<IReadOnlyList<ChatPerson>> FindByNameAsync(string name, int top, CancellationToken cancellationToken) => throw NotSupported("look a person up");

    /// <inheritdoc />
    public Task<string?> FindDirectChatAsync(string userId, CancellationToken cancellationToken) => throw NotSupported("read a place");

    /// <inheritdoc />
    public Task<string> OpenDirectChatAsync(string userId, CancellationToken cancellationToken) => throw NotSupported("open a chat");

    /// <inheritdoc />
    public Task<ChatPage<ChatMessage>> ReadAsync(ChatTarget target, ChatReadQuery query, CancellationToken cancellationToken) => throw NotSupported("read a place");

    /// <inheritdoc />
    public Task<ChatSent> SendAsync(ChatTarget target, ChatOutgoing message, CancellationToken cancellationToken) => throw NotSupported("post in a place that is named");

    /// <inheritdoc />
    public Task<ChatMessage> GetMessageAsync(ChatTarget target, string? thread, string messageId, CancellationToken cancellationToken) => throw NotSupported("read a place");

    /// <inheritdoc />
    public Task<ChatSearchPage> SearchAsync(ChatSearchQuery query, CancellationToken cancellationToken) => throw NotSupported("search messages");

    /// <inheritdoc />
    public Task EditAsync(ChatTarget target, string? thread, string messageId, ChatOutgoing message, CancellationToken cancellationToken) => throw NotSupported("edit a message");

    /// <inheritdoc />
    public Task DeleteAsync(ChatTarget target, string? thread, string messageId, CancellationToken cancellationToken) => throw NotSupported("delete a message");

    /// <inheritdoc />
    public Task ReactAsync(ChatTarget target, string messageId, string reaction, bool remove, CancellationToken cancellationToken) => throw NotSupported("react to a message");

    /// <inheritdoc />
    public Task<ChatMeetingLink?> GetMeetingLinkAsync(string chatId, CancellationToken cancellationToken) => throw NotSupported("find a meeting");

    /// <inheritdoc />
    public Task<ChatMeeting?> FindMeetingAsync(string joinLink, string? organizer, CancellationToken cancellationToken) => throw NotSupported("find a meeting");

    /// <inheritdoc />
    public Task<IReadOnlyList<ChatTranscript>> ListTranscriptsAsync(ChatMeeting meeting, CancellationToken cancellationToken) => throw NotSupported("read a transcript");

    /// <inheritdoc />
    public Task<ChatTranscriptText> ReadTranscriptAsync(ChatMeeting meeting, string transcriptId, CancellationToken cancellationToken) => throw NotSupported("read a transcript");
}
