using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Orkeon.Domain.Attributes;
using Orkeon.Tools.Abstractions.Base;
using Microsoft.Extensions.Logging;

namespace Orkeon.Tools.Web;

// ── Typed Request / Response records ──────────────────────────────────────

/// <summary>
/// Strongly-typed request for SlackTool.
/// </summary>
public sealed class SlackSendMessageRequest
{
    /// <summary>Gets or sets the Slack channel name (e.g., "#general") or user ID.</summary>
    [JsonPropertyName("channel")]
    [FieldSchema(Description = "Slack channel (e.g., '#general', '#dev-team') or user ID (e.g., 'U1234567890')", IsRequired = true, Example = "#general")]
    public string Channel { get; set; } = string.Empty;

    /// <summary>Gets or sets the message text to send.</summary>
    [JsonPropertyName("message")]
    [FieldSchema(Description = "The message text to send", IsRequired = true, Example = "Hello, team!")]
    public string Message { get; set; } = string.Empty;

    /// <summary>Gets or sets optional thread timestamp for threaded replies.</summary>
    [JsonPropertyName("thread_ts")]
    [FieldSchema(Description = "Optional thread timestamp for threaded replies", IsRequired = false, Example = "1234567890.123456")]
    public string? ThreadTs { get; set; }

    /// <summary>Initializes a new instance of <see cref="SlackSendMessageRequest"/>.</summary>
    public SlackSendMessageRequest() { }
}

/// <summary>
/// Strongly-typed response for SlackTool send message operation.
/// </summary>
public sealed class SlackSendMessageResponse
{
    /// <summary>Gets or sets whether the message was sent successfully.</summary>
    [JsonPropertyName("success")]
    [ReturnSchema(Description = "Whether the message was sent successfully", Example = true)]
    public bool Success { get; set; }

    /// <summary>Gets or sets the timestamp of the sent message.</summary>
    [JsonPropertyName("timestamp")]
    [ReturnSchema(Description = "Timestamp of the sent message", Example = "1234567890.123456")]
    public string Timestamp { get; set; } = string.Empty;

    /// <summary>Gets or sets the channel where the message was sent.</summary>
    [JsonPropertyName("channel")]
    [ReturnSchema(Description = "The channel where the message was sent", Example = "#general")]
    public string Channel { get; set; } = string.Empty;

    /// <summary>Gets or sets optional error message if send failed.</summary>
    [JsonPropertyName("error")]
    [ReturnSchema(Description = "Error message if the send operation failed", Example = "")]
    public string? Error { get; set; }
}

// ── Tool implementation ──────────────────────────────────────────────────

/// <summary>
/// Tool for sending messages to Slack channels or users via Slack Web API.
/// Requires a Slack bot token (xoxb-).
/// </summary>
[ToolContract("slack_send_message",
    Name = "slack_send_message",
    Description = "Send messages to Slack channels or users via Slack Web API",
    Category = "Communication")]
public partial class SlackTool : HttpToolBase<SlackSendMessageRequest, SlackSendMessageResponse>
{
    private const string SlackApiBaseUrl = "https://slack.com/api";
    private const string SendMessageEndpoint = "/chat.postMessage";
    private readonly string _botToken;

    /// <summary>Initializes a new instance of <see cref="SlackTool"/>.</summary>
    /// <param name="botToken">The Slack bot token (xoxb-).</param>
    /// <param name="httpClient">Optional <see cref="HttpClient"/> to use for HTTP requests.</param>
    /// <param name="logger">Optional logger instance.</param>
    public SlackTool(string botToken, HttpClient? httpClient = null, ILogger<SlackTool>? logger = null)
        : base(httpClient, logger)
    {
        _botToken = botToken ?? throw new ArgumentNullException(nameof(botToken));
    }

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(SlackSendMessageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Channel))
            return "Channel parameter is required";

        if (string.IsNullOrWhiteSpace(request.Message))
            return "Message parameter is required";

        if (string.IsNullOrWhiteSpace(_botToken))
            return "Slack bot token is not configured";

        if (!_botToken.StartsWith("xoxb-", StringComparison.OrdinalIgnoreCase))
            return "Invalid Slack bot token format. Must start with 'xoxb-'";

        return null;
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Service-boundary fault barrier: any Slack API or parsing failure is converted to a failed SlackSendMessageResponse so one tool call cannot crash the agent loop; timeout/cancellation is preserved by the separate TaskCanceledException filter.")]
    protected override Task<SlackSendMessageResponse> ExecuteTypedAsync(
        SlackSendMessageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTypedCoreAsync();

        async Task<SlackSendMessageResponse> ExecuteTypedCoreAsync()
        {
        try
        {
            var url = $"{SlackApiBaseUrl}{SendMessageEndpoint}";
            var payload = new Dictionary<string, object>
            {
                ["channel"] = request.Channel,
                ["text"] = request.Message
            };

            if (!string.IsNullOrWhiteSpace(request.ThreadTs))
            {
                payload["thread_ts"] = request.ThreadTs;
            }

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
            httpRequest.Headers.Add("Authorization", $"Bearer {_botToken}");
            httpRequest.Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                LogSlackApiError(request.Channel, (int)response.StatusCode, responseBody);
                return new SlackSendMessageResponse
                {
                    Success = false,
                    Error = $"HTTP {(int)response.StatusCode}: {responseBody}"
                };
            }

            var slackResponse = JsonSerializer.Deserialize<SlackApiResponse>(responseBody);

            if (slackResponse == null || !slackResponse.Ok)
            {
                var errorMsg = slackResponse?.Error ?? "Unknown error";
                LogSlackApiError(request.Channel, (int)response.StatusCode, errorMsg);
                return new SlackSendMessageResponse
                {
                    Success = false,
                    Error = errorMsg
                };
            }

            LogMessageSent(request.Channel, slackResponse.Ts ?? "");

            return new SlackSendMessageResponse
            {
                Success = true,
                Timestamp = slackResponse.Ts ?? "",
                Channel = request.Channel
            };
        }
        catch (HttpRequestException ex)
        {
            LogHttpError(request.Channel, ex.Message);
            return new SlackSendMessageResponse
            {
                Success = false,
                Error = $"HTTP request failed: {ex.Message}"
            };
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            LogTimeout(request.Channel);
            return new SlackSendMessageResponse
            {
                Success = false,
                Error = $"Request timed out: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            LogUnexpectedError(request.Channel, ex.Message);
            return new SlackSendMessageResponse
            {
                Success = false,
                Error = $"Unexpected error: {ex.Message}"
            };
        }
        }
    }

    // ── Slack API response model ────────────────────────────────────

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "Instantiated via System.Text.Json deserialization of the Slack API response.")]
    private sealed class SlackApiResponse
    {
        [JsonPropertyName("ok")]
        public bool Ok { get; set; }

        [JsonPropertyName("ts")]
        public string? Ts { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }

    // ── Logging ────────────────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Information, Message = "Message sent to Slack channel '{Channel}' with timestamp {Timestamp}")]
    private partial void LogMessageSent(string channel, string timestamp);

    [LoggerMessage(Level = LogLevel.Error, Message = "Slack API error for channel '{Channel}': HTTP {StatusCode} - {ErrorBody}")]
    private partial void LogSlackApiError(string channel, int statusCode, string errorBody);

    [LoggerMessage(Level = LogLevel.Error, Message = "HTTP request failed for Slack channel '{Channel}': {Error}")]
    private partial void LogHttpError(string channel, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Slack request to channel '{Channel}' timed out")]
    private partial void LogTimeout(string channel);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unexpected error sending message to Slack channel '{Channel}': {Error}")]
    private partial void LogUnexpectedError(string channel, string error);
}
