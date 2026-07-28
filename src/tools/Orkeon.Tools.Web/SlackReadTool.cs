using System.Text.Json;
using System.Text.Json.Serialization;
using Orkeon.Domain.Attributes;
using Orkeon.Tools.Abstractions.Base;
using Microsoft.Extensions.Logging;

namespace Orkeon.Tools.Web;

// ── Typed Request / Response records (SlackReadTool) ───────────────────────

/// <summary>
/// Strongly-typed request for SlackReadTool.
/// </summary>
public sealed class SlackReadMessagesRequest
{
    /// <summary>Gets or sets the Slack channel name to read from.</summary>
    [JsonPropertyName("channel")]
    [FieldSchema(Description = "Slack channel to read from (e.g., '#general', '#dev-team')", IsRequired = true, Example = "#general")]
    public string Channel { get; set; } = string.Empty;

    /// <summary>Gets or sets the maximum number of messages to retrieve.</summary>
    [JsonPropertyName("limit")]
    [FieldSchema(Description = "Maximum number of messages to retrieve (default: 10, max: 100)", IsRequired = false, Example = 10)]
    public int Limit { get; set; } = 10;

    /// <summary>Gets or sets the optional timestamp to get messages after this time.</summary>
    [JsonPropertyName("oldest")]
    [FieldSchema(Description = "Optional timestamp to get messages after this time (Unix epoch)", IsRequired = false, Example = "1234567890")]
    public string? Oldest { get; set; }

    /// <summary>Initializes a new instance of <see cref="SlackReadMessagesRequest"/>.</summary>
    public SlackReadMessagesRequest() { }
}

/// <summary>
/// A single Slack message item.
/// </summary>
public sealed record SlackMessage
{
    /// <summary>Gets the message timestamp.</summary>
    [JsonPropertyName("timestamp")]
    [ReturnSchema(Description = "Message timestamp", Example = "1234567890.123456")]
    public string Timestamp { get; init; } = "";

    /// <summary>Gets the username of the message author.</summary>
    [JsonPropertyName("username")]
    [ReturnSchema(Description = "Username of the message author", Example = "alice")]
    public string Username { get; init; } = "";

    /// <summary>Gets the message text content.</summary>
    [JsonPropertyName("text")]
    [ReturnSchema(Description = "Message text content", Example = "This is a message")]
    public string Text { get; init; } = "";

    /// <summary>Gets the user ID of the message author.</summary>
    [JsonPropertyName("user_id")]
    [ReturnSchema(Description = "User ID of the message author", Example = "U1234567890")]
    public string UserId { get; init; } = "";
}

/// <summary>
/// Strongly-typed response for SlackReadTool.
/// </summary>
public sealed class SlackReadMessagesResponse
{
    /// <summary>Gets or sets whether the read operation was successful.</summary>
    [JsonPropertyName("success")]
    [ReturnSchema(Description = "Whether the read operation was successful", Example = true)]
    public bool Success { get; set; }

    /// <summary>Gets or sets the channel name.</summary>
    [JsonPropertyName("channel")]
    [ReturnSchema(Description = "The channel name", Example = "#general")]
    public string Channel { get; set; } = string.Empty;

    /// <summary>Gets or sets the list of retrieved messages.</summary>
    [JsonPropertyName("messages")]
    [ReturnSchema(Description = "List of retrieved messages")]
    public IReadOnlyList<SlackMessage> Messages { get; init; } = [];

    /// <summary>Gets or sets the number of messages retrieved.</summary>
    [JsonPropertyName("message_count")]
    [ReturnSchema(Description = "Number of messages retrieved", Example = 5)]
    public int MessageCount { get; set; }

    /// <summary>Gets or sets optional error message if read failed.</summary>
    [JsonPropertyName("error")]
    [ReturnSchema(Description = "Error message if the read operation failed", Example = "")]
    public string? Error { get; set; }
}

// ── Tool implementation ────────────────────────────────────────────────────

/// <summary>
/// Tool for reading messages from Slack channels via Slack Web API.
/// Requires a Slack bot token (xoxb-).
/// </summary>
[ToolContract("slack_read_messages",
    Name = "slack_read_messages",
    Description = "Read messages from Slack channels via Slack Web API",
    Category = "Communication")]
public partial class SlackReadTool : HttpToolBase<SlackReadMessagesRequest, SlackReadMessagesResponse>
{
#pragma warning disable S1075 // Canonical vendor API root used as a documented fallback default; configuration overrides it. Not a filesystem path — the VFS policy does not apply.
    private const string SlackApiBaseUrl = "https://slack.com/api";
#pragma warning restore S1075
    private const string ConversationsHistoryEndpoint = "/conversations.history";
    private readonly string _botToken;

    /// <summary>Initializes a new instance of <see cref="SlackReadTool"/>.</summary>
    /// <param name="botToken">The Slack bot token (xoxb-).</param>
    /// <param name="httpClient">Optional <see cref="HttpClient"/> to use for HTTP requests.</param>
    /// <param name="logger">Optional logger instance.</param>
    public SlackReadTool(string botToken, HttpClient? httpClient = null, ILogger<SlackReadTool>? logger = null)
        : base(httpClient, logger)
    {
        _botToken = botToken ?? throw new ArgumentNullException(nameof(botToken));
    }

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(SlackReadMessagesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Channel))
            return "Channel parameter is required";

        if (request.Limit < 1 || request.Limit > 100)
            return "Limit must be between 1 and 100";

        if (string.IsNullOrWhiteSpace(_botToken))
            return "Slack bot token is not configured";

        if (!_botToken.StartsWith("xoxb-", StringComparison.OrdinalIgnoreCase))
            return "Invalid Slack bot token format. Must start with 'xoxb-'";

        return null;
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Service-boundary fault barrier: any Slack API or parsing failure is converted to a failed SlackReadMessagesResponse so one tool call cannot crash the agent loop; timeout/cancellation is preserved by the separate TaskCanceledException filter.")]
    protected override Task<SlackReadMessagesResponse> ExecuteTypedAsync(
        SlackReadMessagesRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTypedCoreAsync();

        async Task<SlackReadMessagesResponse> ExecuteTypedCoreAsync()
        {
        try
        {
            // Resolve channel name to channel ID first
            var channelId = await ResolveChannelIdAsync(request.Channel, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(channelId))
            {
                LogChannelNotFound(request.Channel);
                return new SlackReadMessagesResponse
                {
                    Success = false,
                    Channel = request.Channel,
                    Error = $"Channel '{request.Channel}' not found"
                };
            }

            var url = $"{SlackApiBaseUrl}{ConversationsHistoryEndpoint}";
            var queryParams = new List<string>
            {
                $"channel={Uri.EscapeDataString(channelId)}",
                $"limit={request.Limit}"
            };

            if (!string.IsNullOrWhiteSpace(request.Oldest))
            {
                queryParams.Add($"oldest={Uri.EscapeDataString(request.Oldest)}");
            }

            var fullUrl = $"{url}?{string.Join("&", queryParams)}";

            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, fullUrl);
            httpRequest.Headers.Add("Authorization", $"Bearer {_botToken}");

            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                LogSlackApiError(request.Channel, (int)response.StatusCode, responseBody);
                return new SlackReadMessagesResponse
                {
                    Success = false,
                    Channel = request.Channel,
                    Error = $"HTTP {(int)response.StatusCode}: {responseBody}"
                };
            }

            var slackResponse = JsonSerializer.Deserialize<ConversationsHistoryResponse>(responseBody);

            if (slackResponse == null || !slackResponse.Ok)
            {
                var errorMsg = slackResponse?.Error ?? "Unknown error";
                LogSlackApiError(request.Channel, (int)response.StatusCode, errorMsg);
                return new SlackReadMessagesResponse
                {
                    Success = false,
                    Channel = request.Channel,
                    Error = errorMsg
                };
            }

            var messages = slackResponse.Messages?.Select(m => new SlackMessage
            {
                Timestamp = m.Ts ?? "",
                Username = m.Username ?? m.UserId ?? "unknown",
                Text = m.Text ?? "",
                UserId = m.UserId ?? ""
            }).ToList() ?? [];

            LogMessagesRetrieved(request.Channel, messages.Count);

            return new SlackReadMessagesResponse
            {
                Success = true,
                Channel = request.Channel,
                Messages = messages,
                MessageCount = messages.Count
            };
        }
        catch (HttpRequestException ex)
        {
            LogHttpError(request.Channel, ex.Message);
            return new SlackReadMessagesResponse
            {
                Success = false,
                Channel = request.Channel,
                Error = $"HTTP request failed: {ex.Message}"
            };
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            LogTimeout(request.Channel);
            return new SlackReadMessagesResponse
            {
                Success = false,
                Channel = request.Channel,
                Error = $"Request timed out: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            LogUnexpectedError(request.Channel, ex.Message);
            return new SlackReadMessagesResponse
            {
                Success = false,
                Channel = request.Channel,
                Error = $"Unexpected error: {ex.Message}"
            };
        }
        }
    }

    /// <summary>
    /// Resolves a channel name (e.g., "#general") to its channel ID.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort channel resolution: any failure while listing/parsing Slack conversations yields a null channel id (unresolved) rather than propagating, so the caller surfaces a clean 'channel not found' result.")]
    private async Task<string?> ResolveChannelIdAsync(string channelInput, CancellationToken cancellationToken)
    {
        try
        {
            // If it's already a channel ID (starts with C), return as-is
            if (channelInput.Length > 0 && (channelInput[0] == 'C' || channelInput[0] == 'c'))
            {
                return channelInput;
            }

            // Remove leading # if present
            var channelName = channelInput.StartsWith('#') ? channelInput[1..] : channelInput;

            var url = $"{SlackApiBaseUrl}/conversations.list?exclude_archived=true&limit=1000";

            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);
            httpRequest.Headers.Add("Authorization", $"Bearer {_botToken}");

            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            var slackResponse = JsonSerializer.Deserialize<ConversationsListResponse>(responseBody);
            if (slackResponse?.Channels == null) return null;

            var channel = slackResponse.Channels.FirstOrDefault(c =>
                c.Name?.Equals(channelName, StringComparison.OrdinalIgnoreCase) == true);

            return channel?.Id;
        }
        catch
        {
            return null;
        }
    }

    // ── Slack API response models ────────────────────────────────────

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "Instantiated via System.Text.Json deserialization of the Slack API response.")]
    private sealed class ConversationsHistoryResponse
    {
        [JsonPropertyName("ok")]
        public bool Ok { get; set; }

        [JsonPropertyName("messages")]
        public List<SlackApiMessage>? Messages { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "Instantiated via System.Text.Json deserialization of the Slack API response.")]
    private sealed class SlackApiMessage
    {
        [JsonPropertyName("ts")]
        public string? Ts { get; set; }

        [JsonPropertyName("user")]
        public string? UserId { get; set; }

        [JsonPropertyName("username")]
        public string? Username { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "Instantiated via System.Text.Json deserialization of the Slack API response.")]
    private sealed class ConversationsListResponse
    {
        [JsonPropertyName("ok")]
        public bool Ok { get; set; }

        [JsonPropertyName("channels")]
        public List<SlackChannel>? Channels { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "Instantiated via System.Text.Json deserialization of the Slack API response.")]
    private sealed class SlackChannel
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    // ── Logging ────────────────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Information, Message = "Retrieved {MessageCount} messages from Slack channel '{Channel}'")]
    private partial void LogMessagesRetrieved(string channel, int messageCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Channel '{Channel}' not found")]
    private partial void LogChannelNotFound(string channel);

    [LoggerMessage(Level = LogLevel.Error, Message = "Slack API error for channel '{Channel}': HTTP {StatusCode} - {ErrorBody}")]
    private partial void LogSlackApiError(string channel, int statusCode, string errorBody);

    [LoggerMessage(Level = LogLevel.Error, Message = "HTTP request failed for Slack channel '{Channel}': {Error}")]
    private partial void LogHttpError(string channel, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Slack request to channel '{Channel}' timed out")]
    private partial void LogTimeout(string channel);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unexpected error reading from Slack channel '{Channel}': {Error}")]
    private partial void LogUnexpectedError(string channel, string error);
}
