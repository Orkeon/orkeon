using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Orkeon.Host.Gateway;

/// <summary>Where a reply goes: the service the activity's token was validated for, and the thread it came from.</summary>
internal sealed record TeamsReplyTarget(Uri ServiceUrl, string ConversationId, string ActivityId, string BotId, string UserId);

/// <summary>A message from Teams, with the place its replies go to: it was checked with the message, and travels with it.</summary>
internal sealed record TeamsInboundMessage : InboundMessage
{
    /// <summary>Where replies to this message go.</summary>
    public required TeamsReplyTarget ReplyTo { get; init; }
}

/// <summary>
/// Posts the gateway's replies in the Teams thread they answer, through the Bot Connector API. The address is the one the
/// activity's token was validated for, never one read from a body, and no redirect is followed from it.
/// </summary>
internal sealed partial class TeamsResponder : IChatResponder
{
    /// <summary>Teams refuses a bot message beyond about 100 KB; this many characters stay under it in any script.</summary>
    internal const int MaxTextLength = 20_000;

    private const int Attempts = 3;
    private static readonly TimeSpan s_maxDelay = TimeSpan.FromSeconds(30);

    // Relaxed escaping: the default writes every accented letter as six bytes, and the size limit counts bytes.
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly HttpClient _http;
    private readonly TeamsBotToken _token;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly TeamsFileExchange? _files;

    /// <summary>Builds the responder over the transport that reaches the Bot Connector.</summary>
    /// <param name="http">The transport.</param>
    /// <param name="token">The bot's token.</param>
    /// <param name="time">The clock.</param>
    /// <param name="logger">Where the responder logs.</param>
    /// <param name="files">What sends files, null when the host keeps none.</param>
    public TeamsResponder(HttpClient http, TeamsBotToken token, TimeProvider time, ILogger logger, TeamsFileExchange? files = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _token = token ?? throw new ArgumentNullException(nameof(token));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _files = files;
    }

    /// <inheritdoc />
    public Task AcknowledgeAsync(InboundMessage message, string text, CancellationToken ct) => SendAsync(message, text, 1, ct);

    /// <inheritdoc />
    public Task ProgressAsync(InboundMessage message, string text, CancellationToken ct) => SendAsync(message, text, 1, ct);

    /// <inheritdoc />
    public Task CompleteAsync(InboundMessage message, string text, CancellationToken ct) => SendAsync(message, text, Attempts, ct);

    /// <inheritdoc />
    public Task<IReadOnlyList<FileSendResult>> SendFilesAsync(InboundMessage message, IReadOnlyList<OutboundFile> files, CancellationToken ct) =>
        _files is not null && message is TeamsInboundMessage teams ? _files.OfferAsync(this, teams, files, ct) : FileSendResult.NoneSent(files);

    /// <inheritdoc />
    int IChatResponder.MaxTextLength => MaxTextLength;

    /// <inheritdoc />
    public bool TakesFileCommands => _files is not null;

    private async Task SendAsync(InboundMessage message, string text, int attempts, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message is not TeamsInboundMessage { ReplyTo: { } target })
        {
            LogNoTarget(message.Channel);
            return;
        }

        await PostAsync(target, BodyOf(target, text), attempts, ct).ConfigureAwait(false);
    }

    /// <summary>Posts a text where no message is at hand.</summary>
    internal Task PostTextAsync(TeamsReplyTarget target, string text, CancellationToken ct) => PostAsync(target, BodyOf(target, text), Attempts, ct);

    /// <summary>Posts a card; false when Teams refused it or it could not be sent.</summary>
    internal Task<bool> PostCardAsync(TeamsReplyTarget target, Dictionary<string, object?> card, CancellationToken ct)
    {
        var body = AddressedTo(target);
        body["attachments"] = new object[] { card };
        return PostAsync(target, body, Attempts, ct);
    }

    private async Task<bool> PostAsync(TeamsReplyTarget target, Dictionary<string, object> body, int attempts, CancellationToken ct)
    {
        try
        {
            return await DeliverAsync(target, body, attempts, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            LogSendCancelled(target.ConversationId);
        }
#pragma warning disable CA1031 // A channel that refuses a reply must not fail the run.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogSendFailed(ex, target.ConversationId);
        }

        return false;
    }

    private async Task<bool> DeliverAsync(TeamsReplyTarget target, Dictionary<string, object> body, int attempts, CancellationToken ct)
    {
        var renewed = false;
        for (var attempt = 1; ;)
        {
            string token;
            HttpResponseMessage answer;
            try
            {
                token = await _token.GetAsync(ct).ConfigureAwait(false);
                answer = await SendOnceAsync(target, body, token, ct).ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (attempt < attempts && IsTransient(ex))
            {
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt), _time, ct).ConfigureAwait(false);
                attempt++;
                continue;
            }

            using var response = answer;
            if (response.IsSuccessStatusCode)
                return true;

            if (response.StatusCode == HttpStatusCode.Unauthorized && !renewed)
            {
                renewed = true;
                await _token.ForgetAsync(token, ct).ConfigureAwait(false);
                continue;
            }

            if (attempt >= attempts || !IsTransient(response.StatusCode))
            {
                LogSendRefused(target.ConversationId, (int)response.StatusCode);
                return false;
            }

            await Task.Delay(DelayOf(response, attempt), _time, ct).ConfigureAwait(false);
            attempt++;
        }
    }

    private async Task<HttpResponseMessage> SendOnceAsync(TeamsReplyTarget target, Dictionary<string, object> body, string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, AddressOf(target));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(body, options: s_json);
        return await _http.SendAsync(request, ct).ConfigureAwait(false);
    }

    private static Uri AddressOf(TeamsReplyTarget target) =>
        new($"{target.ServiceUrl.AbsoluteUri.TrimEnd('/')}/v3/conversations/{Uri.EscapeDataString(target.ConversationId)}"
            + $"/activities/{Uri.EscapeDataString(target.ActivityId)}");

    private static Dictionary<string, object> BodyOf(TeamsReplyTarget target, string text)
    {
        var body = AddressedTo(target);
        body["textFormat"] = "markdown";
        body["text"] = TeamsMarkdown.Harmless(DiscordResponder.Truncate(text, MaxTextLength));
        return body;
    }

    private static Dictionary<string, object> AddressedTo(TeamsReplyTarget target) => new()
    {
        ["type"] = "message",
        ["from"] = new { id = target.BotId },
        ["recipient"] = new { id = target.UserId },
        ["conversation"] = new { id = target.ConversationId },
        ["replyToId"] = target.ActivityId,
    };

    private static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.PreconditionFailed or HttpStatusCode.TooManyRequests or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;

    /// <summary>
    /// Whether nothing was posted yet: no connection was made, or the sign-in service is at
    /// fault (the one failure here that carries a status). A connection lost
    /// after the request left may have posted it, and the same text twice is worse than a failure in the log.
    /// </summary>
    private static bool IsTransient(HttpRequestException ex) =>
        ex.StatusCode is not null
        || ex.HttpRequestError is HttpRequestError.NameResolutionError or HttpRequestError.ConnectionError or HttpRequestError.SecureConnectionError;

    private static TimeSpan DelayOf(HttpResponseMessage response, int attempt)
    {
        var asked = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2 * attempt);
        return asked > s_maxDelay ? s_maxDelay : asked;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A reply to a message of the channel {Channel} has no Teams conversation to go to")]
    private partial void LogNoTarget(string channel);

    [LoggerMessage(Level = LogLevel.Debug, Message = "A Teams message to {ConversationId} was cancelled")]
    private partial void LogSendCancelled(string conversationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Teams refused a message to {ConversationId}: HTTP {Status}")]
    private partial void LogSendRefused(string conversationId, int status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to send a Teams message to {ConversationId}")]
    private partial void LogSendFailed(Exception ex, string conversationId);
}
