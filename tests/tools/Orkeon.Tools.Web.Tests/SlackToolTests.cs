using Orkeon.Domain.Tools.Protocol;
using System.Net;
using Orkeon.Tools.Web.Tests.Doubles;

namespace Orkeon.Tools.Web.Tests;

public sealed class SlackToolTests : IDisposable
{
    private readonly MockHttpMessageHandler _mockHandler;
    private readonly HttpClient _httpClient;
    private readonly SlackTool _tool;

    public SlackToolTests()
    {
        _mockHandler = new MockHttpMessageHandler();
        _httpClient = new HttpClient(_mockHandler, disposeHandler: false);
        _tool = new SlackTool("xoxb-test-token-12345", _httpClient);
    }

    [Fact]
    public async Task ShouldSendMessage_WhenCalledWithValidParameters()
    {
        var slackResponse = """{"ok": true, "ts": "1234567890.123456", "channel": "C1234567890"}""";
        _mockHandler.SetResponse(HttpStatusCode.OK, slackResponse);

        var request = new ToolCallRequest(
            ToolName: "slack_send_message",
            Parameters: new Dictionary<string, object?>
            {
                ["channel"] = "#general",
                ["message"] = "Hello team!"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal("1234567890.123456", dict["timestamp"]!.ToString());
        Assert.Equal("#general", dict["channel"]!.ToString());
    }

    [Fact]
    public async Task ShouldSendThreadReply_WhenThreadTsIsProvided()
    {
        var slackResponse = """{"ok": true, "ts": "1234567890.654321", "channel": "C1234567890"}""";
        _mockHandler.SetResponse(HttpStatusCode.OK, slackResponse);

        var request = new ToolCallRequest(
            ToolName: "slack_send_message",
            Parameters: new Dictionary<string, object?>
            {
                ["channel"] = "#general",
                ["message"] = "Reply in thread",
                ["thread_ts"] = "1234567890.123456"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal("1234567890.654321", dict["timestamp"]!.ToString());
    }

    [Fact]
    public async Task ShouldReturnError_WhenChannelIsEmpty()
    {
        var request = new ToolCallRequest(
            ToolName: "slack_send_message",
            Parameters: new Dictionary<string, object?>
            {
                ["message"] = "Hello"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("channel", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldReturnError_WhenMessageIsEmpty()
    {
        var request = new ToolCallRequest(
            ToolName: "slack_send_message",
            Parameters: new Dictionary<string, object?>
            {
                ["channel"] = "#general"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("message", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldReturnError_WhenSlackApiReturnsFailure()
    {
        var slackResponse = """{"ok": false, "error": "channel_not_found"}""";
        _mockHandler.SetResponse(HttpStatusCode.OK, slackResponse);

        var request = new ToolCallRequest(
            ToolName: "slack_send_message",
            Parameters: new Dictionary<string, object?>
            {
                ["channel"] = "#nonexistent",
                ["message"] = "Test"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("channel_not_found", result.Error);
    }

    [Fact]
    public async Task ShouldReturnError_WhenHttpStatusCodeIsBadRequest()
    {
        _mockHandler.SetResponse(HttpStatusCode.BadRequest, """{"ok": false, "error": "invalid_arg_name"}""");

        var request = new ToolCallRequest(
            ToolName: "slack_send_message",
            Parameters: new Dictionary<string, object?>
            {
                ["channel"] = "#general",
                ["message"] = "Test"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("HTTP", result.Error);
    }

    [Fact]
    public async Task ShouldReturnError_WhenBotTokenIsInvalid()
    {
        using var tool = new SlackTool("invalid-token", _httpClient);

        var request = new ToolCallRequest(
            ToolName: "slack_send_message",
            Parameters: new Dictionary<string, object?>
            {
                ["channel"] = "#general",
                ["message"] = "Test"
            }
        );

        var result = await tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("bot token", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShouldHaveCorrectConfiguration_WhenAccessingSchema()
    {
        Assert.Equal("slack_send_message", _tool.Name);
        Assert.Equal("Communication", _tool.Category);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _httpClient.Dispose();
        _mockHandler.Dispose();
        _tool.Dispose();
    }
}

public sealed class SlackReadToolTests : IDisposable
{
    private readonly MockHttpMessageHandler _mockHandler;
    private readonly HttpClient _httpClient;
    private readonly SlackReadTool _tool;

    public SlackReadToolTests()
    {
        _mockHandler = new MockHttpMessageHandler();
        _httpClient = new HttpClient(_mockHandler, disposeHandler: false);
        _tool = new SlackReadTool("xoxb-test-token-12345", _httpClient);
    }

    // ── Happy path ────────────────────────────────────────────────────

    [Fact]
    public async Task ShouldRetrieveMessages_WhenChannelNameResolvesToId()
    {
        var listResponse = """
        {
            "ok": true,
            "channels": [
                {"id": "C1234567890", "name": "general"}
            ]
        }
        """;

        var historyResponse = """
        {
            "ok": true,
            "messages": [
                {
                    "ts": "1234567890.123456",
                    "user": "U1111111111",
                    "text": "Hello everyone!"
                },
                {
                    "ts": "1234567890.654321",
                    "user": "U2222222222",
                    "text": "Hi there!"
                }
            ]
        }
        """;

        _mockHandler.SetResponseFactory(request =>
        {
            if (request.RequestUri?.PathAndQuery.Contains("conversations.list") == true)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(listResponse)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(historyResponse)
            };
        });

        var request = new ToolCallRequest(
            ToolName: "slack_read_messages",
            Parameters: new Dictionary<string, object?>
            {
                ["channel"] = "#general",
                ["limit"] = 10
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal("#general", dict["channel"]!.ToString());
        Assert.Equal(2, (int)dict["message_count"]!);
    }

    [Fact]
    public async Task ShouldSkipChannelResolution_WhenChannelStartsWithC()
    {
        // When channel already looks like an ID (starts with C), no conversations.list call needed
        var historyResponse = """
        {
            "ok": true,
            "messages": [
                {
                    "ts": "1111111111.000001",
                    "user": "U9999999999",
                    "username": "alice",
                    "text": "Direct channel ID message"
                }
            ]
        }
        """;

        _mockHandler.SetResponse(HttpStatusCode.OK, historyResponse);

        var request = new ToolCallRequest(
            ToolName: "slack_read_messages",
            Parameters: new Dictionary<string, object?>
            {
                ["channel"] = "C1234567890",
                ["limit"] = 5
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal(1, (int)dict["message_count"]!);

        // Only one HTTP call (conversations.history), no conversations.list
        Assert.Single(_mockHandler.AllRequests);
        Assert.Contains("conversations.history", _mockHandler.LastRequest!.RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task ShouldReturnEmptyMessages_WhenChannelHasNoMessages()
    {
        var historyResponse = """
        {
            "ok": true,
            "messages": []
        }
        """;

        _mockHandler.SetResponse(HttpStatusCode.OK, historyResponse);

        var request = new ToolCallRequest(
            ToolName: "slack_read_messages",
            Parameters: new Dictionary<string, object?>
            {
                ["channel"] = "C1234567890",
                ["limit"] = 10
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal(0, (int)dict["message_count"]!);
    }

    [Fact]
    public async Task ShouldIncludeOldestParam_WhenOldestTimestampProvided()
    {
        var historyResponse = """
        {
            "ok": true,
            "messages": [
                {
                    "ts": "1700000000.000001",
                    "user": "U1111111111",
                    "text": "Recent message"
                }
            ]
        }
        """;

        _mockHandler.SetResponse(HttpStatusCode.OK, historyResponse);

        var request = new ToolCallRequest(
            ToolName: "slack_read_messages",
            Parameters: new Dictionary<string, object?>
            {
                ["channel"] = "C1234567890",
                ["limit"] = 10,
                ["oldest"] = "1699999999"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);

        // Verify oldest was included in the query string
        var requestUri = _mockHandler.LastRequest!.RequestUri!.ToString();
        Assert.Contains("oldest=1699999999", requestUri);
    }

    [Fact]
    public async Task ShouldMapMessageFields_WhenMessagesHaveUserAndUsername()
    {
        var historyResponse = """
        {
            "ok": true,
            "messages": [
                {
                    "ts": "1234567890.123456",
                    "user": "U1111111111",
                    "username": "alice",
                    "text": "Hello from Alice"
                },
                {
                    "ts": "1234567890.654321",
                    "user": "U2222222222",
                    "text": "Hello from user without username"
                }
            ]
        }
        """;

        _mockHandler.SetResponse(HttpStatusCode.OK, historyResponse);

        var request = new ToolCallRequest(
            ToolName: "slack_read_messages",
            Parameters: new Dictionary<string, object?>
            {
                ["channel"] = "C1234567890",
                ["limit"] = 10
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal(2, (int)dict["message_count"]!);
    }

    [Fact]
    public async Task ShouldSendAuthorizationHeader_WithBearerToken()
    {
        var historyResponse = """
        {
            "ok": true,
            "messages": []
        }
        """;

        _mockHandler.SetResponse(HttpStatusCode.OK, historyResponse);

        var request = new ToolCallRequest(
            ToolName: "slack_read_messages",
            Parameters: new Dictionary<string, object?>
            {
                ["channel"] = "C1234567890",
                ["limit"] = 10
            }
        );

        await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        var authHeader = _mockHandler.LastRequest!.Headers.Authorization;
        Assert.NotNull(authHeader);
        Assert.Equal("Bearer", authHeader.Scheme);
        Assert.Equal("xoxb-test-token-12345", authHeader.Parameter);
    }

    // ── Channel resolution ────────────────────────────────────────────

    [Fact]
    public async Task ShouldReturnError_WhenChannelNameNotFound()
    {
        var listResponse = """
        {
            "ok": true,
            "channels": []
        }
        """;

        _mockHandler.SetResponse(HttpStatusCode.OK, listResponse);

        var request = new ToolCallRequest(
            ToolName: "slack_read_messages",
            Parameters: new Dictionary<string, object?>
            {
                ["channel"] = "#nonexistent",
                ["limit"] = 10
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldResolveChannelName_WithoutHashPrefix()
    {
        var listResponse = """
        {
            "ok": true,
            "channels": [
                {"id": "C9876543210", "name": "dev-team"}
            ]
        }
        """;

        var historyResponse = """
        {
            "ok": true,
            "messages": [
                {
                    "ts": "1700000000.000001",
                    "user": "U1111111111",
                    "text": "Dev message"
                }
            ]
        }
        """;

        _mockHandler.SetResponseFactory(req =>
        {
            if (req.RequestUri?.PathAndQuery.Contains("conversations.list") == true)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(listResponse)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(historyResponse)
            };
        });

        var request = new ToolCallRequest(
            ToolName: "slack_read_messages",
            Parameters: new Dictionary<string, object?>
            {
                ["channel"] = "dev-team",
                ["limit"] = 10
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal(1, (int)dict["message_count"]!);
    }

    // ── Validation errors ──────────────────────────────────────────────

    [Fact]
    public async Task ShouldReturnError_WhenChannelIsEmpty()
    {
        var request = new ToolCallRequest(
            ToolName: "slack_read_messages",
            Parameters: new Dictionary<string, object?>
            {
                ["limit"] = 10
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("channel", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldReturnError_WhenLimitExceeds100()
    {
        var request = new ToolCallRequest(
            ToolName: "slack_read_messages",
            Parameters: new Dictionary<string, object?>
            {
                ["channel"] = "#general",
                ["limit"] = 101
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("between 1 and 100", result.Error);
    }

    [Fact]
    public async Task ShouldReturnError_WhenLimitIsZero()
    {
        var request = new ToolCallRequest(
            ToolName: "slack_read_messages",
            Parameters: new Dictionary<string, object?>
            {
                ["channel"] = "#general",
                ["limit"] = 0
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("between 1 and 100", result.Error);
    }

    [Fact]
    public async Task ShouldReturnError_WhenBotTokenIsInvalid()
    {
        var tool = new SlackReadTool("invalid-token", _httpClient);

        var request = new ToolCallRequest(
            ToolName: "slack_read_messages",
            Parameters: new Dictionary<string, object?>
            {
                ["channel"] = "#general",
                ["limit"] = 10
            }
        );

        var result = await tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("bot token", result.Error, StringComparison.OrdinalIgnoreCase);

        tool.Dispose();
    }

    [Fact]
    public void ShouldThrow_WhenBotTokenIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new SlackReadTool(null!, _httpClient));
    }

    // ── HTTP / API errors ──────────────────────────────────────────────

    [Fact]
    public async Task ShouldReturnError_WhenHttpStatusIsUnauthorized()
    {
        _mockHandler.SetResponse(HttpStatusCode.Unauthorized, """{"ok": false, "error": "invalid_auth"}""");

        var request = new ToolCallRequest(
            ToolName: "slack_read_messages",
            Parameters: new Dictionary<string, object?>
            {
                ["channel"] = "C1234567890",
                ["limit"] = 10
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("HTTP 401", result.Error);
    }

    [Fact]
    public async Task ShouldReturnError_WhenHttpStatusIsRateLimited()
    {
        _mockHandler.SetResponse(HttpStatusCode.TooManyRequests, """{"ok": false, "error": "ratelimited"}""");

        var request = new ToolCallRequest(
            ToolName: "slack_read_messages",
            Parameters: new Dictionary<string, object?>
            {
                ["channel"] = "C1234567890",
                ["limit"] = 10
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("HTTP 429", result.Error);
    }

    [Fact]
    public async Task ShouldReturnError_WhenSlackApiReturnsOkFalse()
    {
        var errorResponse = """
        {
            "ok": false,
            "error": "channel_not_found"
        }
        """;

        _mockHandler.SetResponse(HttpStatusCode.OK, errorResponse);

        var request = new ToolCallRequest(
            ToolName: "slack_read_messages",
            Parameters: new Dictionary<string, object?>
            {
                ["channel"] = "C1234567890",
                ["limit"] = 10
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("channel_not_found", result.Error);
    }

    [Fact]
    public async Task ShouldReturnError_WhenHttpRequestExceptionOccurs()
    {
        // Use a handler that throws HttpRequestException to simulate network failure
        using var throwingHandler = new ThrowingHttpMessageHandler(
            new HttpRequestException("Connection refused"));
        using var throwingClient = new HttpClient(throwingHandler);
        using var throwingTool = new SlackReadTool("xoxb-test-token-12345", throwingClient);

        var request = new ToolCallRequest(
            ToolName: "slack_read_messages",
            Parameters: new Dictionary<string, object?>
            {
                ["channel"] = "C1234567890",
                ["limit"] = 10
            }
        );

        var result = await throwingTool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("HTTP request failed", result.Error);
        Assert.Contains("Connection refused", result.Error);
    }

    [Fact]
    public async Task ShouldReturnError_WhenRequestTimesOut()
    {
        // TaskCanceledException with a non-cancelled token simulates a timeout
        using var throwingHandler = new ThrowingHttpMessageHandler(
            new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"));
        using var throwingClient = new HttpClient(throwingHandler);
        using var throwingTool = new SlackReadTool("xoxb-test-token-12345", throwingClient);

        var request = new ToolCallRequest(
            ToolName: "slack_read_messages",
            Parameters: new Dictionary<string, object?>
            {
                ["channel"] = "C1234567890",
                ["limit"] = 10
            }
        );

        var result = await throwingTool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        // The tool catches TaskCanceledException (when not user-cancelled) as a timeout
        Assert.True(
            result.Error.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
            result.Error.Contains("Unexpected error", StringComparison.OrdinalIgnoreCase),
            $"Expected timeout or unexpected error message, got: {result.Error}");
    }

    [Fact]
    public async Task ShouldReturnError_WhenUnexpectedExceptionOccurs()
    {
        using var throwingHandler = new ThrowingHttpMessageHandler(
            new InvalidOperationException("Unexpected internal error"));
        using var throwingClient = new HttpClient(throwingHandler);
        using var throwingTool = new SlackReadTool("xoxb-test-token-12345", throwingClient);

        var request = new ToolCallRequest(
            ToolName: "slack_read_messages",
            Parameters: new Dictionary<string, object?>
            {
                ["channel"] = "C1234567890",
                ["limit"] = 10
            }
        );

        var result = await throwingTool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Unexpected error", result.Error);
    }

    [Fact]
    public async Task ShouldReturnError_WhenConversationsListFailsDuringResolution()
    {
        // If conversations.list returns null channels, channel resolution returns null
        var listResponse = """
        {
            "ok": false,
            "error": "token_revoked"
        }
        """;

        _mockHandler.SetResponse(HttpStatusCode.OK, listResponse);

        var request = new ToolCallRequest(
            ToolName: "slack_read_messages",
            Parameters: new Dictionary<string, object?>
            {
                ["channel"] = "#general",
                ["limit"] = 10
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    // ── Schema / configuration ─────────────────────────────────────────

    [Fact]
    public void ShouldHaveCorrectToolName()
    {
        Assert.Equal("slack_read_messages", _tool.Name);
    }

    [Fact]
    public void ShouldHaveCorrectCategory()
    {
        Assert.Equal("Communication", _tool.Category);
    }

    [Fact]
    public void ShouldExposeSchemaWithRequiredChannelParameter()
    {
        var schema = _tool.Schema;
        Assert.NotNull(schema);
        Assert.True(schema.Parameters.ContainsKey("channel"));
        Assert.True(schema.Parameters["channel"].Required);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _httpClient.Dispose();
        _mockHandler.Dispose();
        _tool.Dispose();
    }

    /// <summary>
    /// HttpMessageHandler that throws a configured exception on every SendAsync call.
    /// Used to test HttpRequestException, TaskCanceledException, and other failure paths.
    /// </summary>
    private sealed class ThrowingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Exception _exception;

        public ThrowingHttpMessageHandler(Exception exception)
        {
            _exception = exception;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw _exception;
        }
    }
}
