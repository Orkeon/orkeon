# Slack tools (Orkeon.Tools.Web)

`Orkeon.Tools.Web` ships two complementary Slack tools that let Orkeon agents interact with Slack
channels: `slack_send_message` (`SlackTool`) and `slack_read_messages` (`SlackReadTool`). Both are
opt-in — nothing registers them unless the host calls the DI extensions below.

## Tools

### SlackTool - Send Messages

**Class:** `SlackTool`

**Purpose:** Send messages to Slack channels or users via the Slack Web API.

**Requirements:**
- Slack bot token (format: `xoxb-...`)
- Bot must be installed in the target Slack workspace
- Bot must have permissions: `chat:write`, `chat:write.public`

**Features:**
- Send messages to channels (e.g., `#general`) or direct messages to users
- Support for threaded replies using `thread_ts` parameter
- Rich error messages for debugging
- Proper logging of successful sends

**Type Definitions:**

```csharp
// Request
public sealed class SlackSendMessageRequest
{
    public string Channel { get; set; }              // e.g., "#general" or "U1234567890"
    public string Message { get; set; }              // Message text
    public string? ThreadTs { get; set; }            // Optional: timestamp for threaded reply
}

// Response
public sealed class SlackSendMessageResponse
{
    public bool Success { get; set; }
    public string Timestamp { get; set; }            // Message timestamp from Slack
    public string Channel { get; set; }
    public string? Error { get; set; }
}
```

**Example Usage:**

```csharp
// Register in DI
services.AddOrkeonSlackTool("xoxb-your-token");

// Use in an agent
var request = new ToolCallRequest(
    ToolName: "slack_send_message",
    Parameters: new Dictionary<string, object>
    {
        ["channel"] = "#general",
        ["message"] = "Team update: Project X is on track"
    }
);

var response = await slackTool.CallAsync(request);
```

### SlackReadTool - Read Messages

**Class:** `SlackReadTool`

**Purpose:** Read recent messages from Slack channels via the Slack Web API.

**Requirements:**
- Slack bot token (format: `xoxb-...`)
- Bot must be installed in the target Slack workspace
- Bot must have permissions: `channels:history`, `groups:history`

**Features:**
- Retrieve recent messages from channels
- Automatic channel name-to-ID resolution (supports both `#channel` and `C1234567890` formats)
- Configurable message limit (1-100)
- Optional timestamp filtering for retrieving messages after a specific time
- Proper parsing of message metadata (user ID, username, timestamp)

**Type Definitions:**

```csharp
// Request
public sealed class SlackReadMessagesRequest
{
    public string Channel { get; set; }              // e.g., "#general"
    public int Limit { get; set; } = 10;             // 1-100
    public string? Oldest { get; set; }              // Optional: Unix timestamp
}

// Response
public sealed class SlackReadMessagesResponse
{
    public bool Success { get; set; }
    public string Channel { get; set; }
    public List<SlackMessage> Messages { get; set; }
    public int MessageCount { get; set; }
    public string? Error { get; set; }
}

// Message item
public sealed record SlackMessage
{
    public string Timestamp { get; init; }           // e.g., "1234567890.123456"
    public string Username { get; init; }
    public string Text { get; init; }
    public string UserId { get; init; }
}
```

**Example Usage:**

```csharp
// Register in DI
services.AddOrkeonSlackReadTool("xoxb-your-token");

// Use in an agent
var request = new ToolCallRequest(
    ToolName: "slack_read_messages",
    Parameters: new Dictionary<string, object>
    {
        ["channel"] = "#general",
        ["limit"] = 5
    }
);

var response = await slackReadTool.CallAsync(request);
```

## Setup Instructions

### 1. Create a Slack App

1. Go to [api.slack.com/apps](https://api.slack.com/apps)
2. Click "Create New App"
3. Choose "From scratch"
4. Name your app and select your workspace

### 2. Configure Permissions

In your app's OAuth & Permissions page, add these **Bot Token Scopes**:

For SlackTool (send messages):
- `chat:write`
- `chat:write.public`

For SlackReadTool (read messages):
- `channels:history`
- `groups:history`
- `conversations:read` (recommended for better channel resolution)

### 3. Install the App

1. Click "Install to Workspace"
2. Authorize the requested scopes
3. Copy the "Bot User OAuth Token" (starts with `xoxb-`)
4. Store it securely (e.g., environment variables)

### 4. Configure in Your Application

```csharp
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Tools.Web.DependencyInjection;

var services = new ServiceCollection();

var slackToken = Environment.GetEnvironmentVariable("SLACK_BOT_TOKEN");

// Register Slack tools
services.AddOrkeonSlackTool(slackToken);
services.AddOrkeonSlackReadTool(slackToken);

// Use in your agent setup...
```

## Error Handling

Both tools return structured error responses:

```csharp
if (!response.Success)
{
    Console.WriteLine($"Error: {response.Error}");
    // Common errors:
    // - "channel_not_found": Channel doesn't exist or bot isn't a member
    // - "not_in_channel": Bot hasn't joined the channel
    // - "restricted_action": Bot lacks required permissions
    // - "account_inactive": Slack workspace is inactive
}
```

## Integration Examples

### Example 1: Notify Team of Completion

```csharp
var slackTool = /* get from DI */;
var request = new ToolCallRequest(
    ToolName: "slack_send_message",
    Parameters: new Dictionary<string, object>
    {
        ["channel"] = "#project-updates",
        ["message"] = "Analysis complete. See results in pinned message."
    }
);

await slackTool.CallAsync(request);
```

### Example 2: Read Recent Channel Activity

```csharp
var slackReadTool = /* get from DI */;
var request = new ToolCallRequest(
    ToolName: "slack_read_messages",
    Parameters: new Dictionary<string, object>
    {
        ["channel"] = "#announcements",
        ["limit"] = 20
    }
);

var response = await slackReadTool.CallAsync(request);
foreach (var msg in response.Messages)
{
    Console.WriteLine($"[{msg.Username}]: {msg.Text}");
}
```

### Example 3: Thread Conversation

```csharp
// First send a message
var initialRequest = new ToolCallRequest(
    ToolName: "slack_send_message",
    Parameters: new Dictionary<string, object>
    {
        ["channel"] = "#discussion",
        ["message"] = "Starting discussion about Q2 planning"
    }
);

var initialResponse = await slackTool.CallAsync(initialRequest);
var threadTimestamp = initialResponse.Result["timestamp"];

// Later, reply in the thread
var replyRequest = new ToolCallRequest(
    ToolName: "slack_send_message",
    Parameters: new Dictionary<string, object>
    {
        ["channel"] = "#discussion",
        ["message"] = "Here are my thoughts...",
        ["thread_ts"] = threadTimestamp
    }
);

await slackTool.CallAsync(replyRequest);
```

## Testing

Unit tests live in `tests/tools/Orkeon.Tools.Web.Tests/SlackToolTests.cs`.

Run tests with:
```bash
dotnet test tests/tools/Orkeon.Tools.Web.Tests/Orkeon.Tools.Web.Tests.csproj
```

## API Documentation

For more details on the Slack API:
- [chat.postMessage](https://api.slack.com/methods/chat.postMessage)
- [conversations.history](https://api.slack.com/methods/conversations.history)
- [conversations.list](https://api.slack.com/methods/conversations.list)
- [Bot Token Scopes](https://api.slack.com/scopes)

## Best Practices

1. **Security:** Always store bot tokens in secure environment variables, not in code
2. **Rate Limiting:** Slack has rate limits; consider adding delays between calls
3. **Channel Resolution:** Use channel names (`#general`) for readability; the tool handles ID resolution
4. **Error Messages:** Always check `Success` and `Error` fields in responses
5. **Permissions:** Ensure your bot has all required scopes installed before use
6. **Timeouts:** Network requests have default timeouts; adjust via HttpClient configuration if needed
