using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.ToolCalling;

namespace Orkeon.Infrastructure.Tests.LLMs.ToolCalling;

/// <summary>
/// Integration tests validating the full native tool calling pipeline
/// (formatter → provider payload → parser) using mocked HTTP responses.
/// </summary>
public class ToolCallingIntegrationTests
{
    #region Helpers

    private static readonly ToolSchema SampleToolSchema = new(
        Name: "directory_read",
        Description: "Lists files in a directory",
        Parameters: new Dictionary<string, ParameterSchema>
        {
            ["path"] = new ParameterSchema(
                Type: "string",
                Description: "The directory path",
                Required: true)
        });

    private static readonly ToolSchema SecondToolSchema = new(
        Name: "file_read",
        Description: "Reads a file",
        Parameters: new Dictionary<string, ParameterSchema>
        {
            ["file_path"] = new ParameterSchema(
                Type: "string",
                Description: "The file path to read",
                Required: true),
            ["encoding"] = new ParameterSchema(
                Type: "string",
                Description: "File encoding",
                Required: false,
                Default: "utf-8")
        });

    private const string TextResponse = """
        {"choices":[{"message":{"role":"assistant","content":"Hello!"}}],"usage":{"total_tokens":10}}
        """;

    private const string SingleToolCallResponse = """
        {"choices":[{"message":{"role":"assistant","content":null,"tool_calls":[{"id":"call_123","type":"function","function":{"name":"directory_read","arguments":"{\"path\":\"/src\"}"}}]}}],"usage":{"total_tokens":15}}
        """;

    private const string MultipleToolCallsResponse = """
        {"choices":[{"message":{"role":"assistant","content":null,"tool_calls":[{"id":"call_A1","type":"function","function":{"name":"directory_read","arguments":"{\"path\":\"/src\"}"}},{"id":"call_B2","type":"function","function":{"name":"file_read","arguments":"{\"file_path\":\"/src/main.cs\",\"encoding\":\"utf-8\"}"}}]}}],"usage":{"total_tokens":20}}
        """;

    private static LlmConfig CreateConfig(IReadOnlyList<ToolSchema>? tools = null, ToolCallMode toolMode = ToolCallMode.Auto)
    {
#pragma warning disable CS0618 // Type or member is obsolete
        return LlmConfig.Create("gpt-4", "test-api-key-fake") with
        {
            BaseUrl = new Uri("https://api.openai.com/v1"),
            Tools = tools,
            ToolMode = toolMode,
            TimeoutSeconds = 10
        };
#pragma warning restore CS0618
    }

    private static OpenAIToolCallingStrategy CreateStrategy()
        => new OpenAIToolCallingStrategy(NullLogger<OpenAIToolCallParser>.Instance);

    private static SingleClientFactory CreateMockFactory(MockHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.openai.com")
        };
        return new SingleClientFactory(httpClient);
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public SingleClientFactory(HttpClient client) { _client = client; }
        public HttpClient CreateClient(string name) => _client;
    }

    /// <summary>
    /// Mock HTTP handler that captures request bodies and returns queued responses.
    /// </summary>
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        public string? CapturedRequestBody { get; private set; }
        public List<string> AllCapturedRequestBodies { get; } = new();
        private readonly Queue<string> _responses = new();

        public void EnqueueResponse(string json)
        {
            _responses.Enqueue(json);
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                CapturedRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
                AllCapturedRequestBodies.Add(CapturedRequestBody);
            }

            var responseJson = _responses.Count > 0 ? _responses.Dequeue() : TextResponse;
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
            };
        }
    }

    #endregion

    #region Test 1: ToolsIncludedInPayload

    [Fact]
    public async Task ToolsIncludedInPayload_WhenChatAsyncCalledWithToolMetadata()
    {
        // Arrange
        using var handler = new MockHttpMessageHandler();
        handler.EnqueueResponse(TextResponse);

        var factory = CreateMockFactory(handler);
        var strategy = CreateStrategy();
        var config = CreateConfig(tools: new List<ToolSchema> { SampleToolSchema }, toolMode: ToolCallMode.Auto);

        using var provider = new OpenAIProvider(config, factory, strategy);

        // Use ChatAsync with tool metadata to trigger the multi-turn path that injects tools
        var messages = new[]
        {
            LlmMessage.User("List files in /src"),
            new LlmMessage
            {
                Role = "assistant",
                Content = null!,
                RawToolCalls = """[{"id":"call_prev","type":"function","function":{"name":"directory_read","arguments":"{\"path\":\"/src\"}"}}]"""
            },
            new LlmMessage
            {
                Role = "tool",
                Content = "file1.cs\nfile2.cs",
                ToolCallId = "call_prev"
            }
        };

        // Act
        await provider.ChatAsync(messages, config, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(handler.CapturedRequestBody);
        using var doc = JsonDocument.Parse(handler.CapturedRequestBody);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("tools", out var toolsArray),
            "Payload should contain 'tools' array");
        Assert.Equal(JsonValueKind.Array, toolsArray.ValueKind);
        Assert.Equal(1, toolsArray.GetArrayLength());

        Assert.True(root.TryGetProperty("tool_choice", out var toolChoice),
            "Payload should contain 'tool_choice'");
        Assert.Equal("auto", toolChoice.GetString());

        // Verify the tool definition structure
        var firstTool = toolsArray[0];
        Assert.Equal("function", firstTool.GetProperty("type").GetString());
        Assert.Equal("directory_read",
            firstTool.GetProperty("function").GetProperty("name").GetString());
    }

    #endregion

    #region Test 2: SingleToolCallExecutedAndResultReturned

    [Fact]
    public async Task SingleToolCallExecutedAndResultReturned_MultiTurnChatAsync()
    {
        // Arrange
        using var handler = new MockHttpMessageHandler();
        // First call returns a tool call
        handler.EnqueueResponse(SingleToolCallResponse);
        // Second call returns a text response after receiving the tool result
        handler.EnqueueResponse(TextResponse);

        var factory = CreateMockFactory(handler);
        var strategy = CreateStrategy();
        var config = CreateConfig(tools: new List<ToolSchema> { SampleToolSchema });

        using var provider = new OpenAIProvider(config, factory, strategy);

        // --- Turn 1: user prompt → LLM returns tool call ---
        var firstResponse = await provider.GenerateAsync("List files in /src", config, TestContext.Current.CancellationToken);

        Assert.NotNull(firstResponse.RawResponseBody);

        // Parse tool calls from the raw response
        using var firstDoc = JsonDocument.Parse(firstResponse.RawResponseBody);
        var toolCalls = strategy.Parser.ParseToolCalls(firstDoc.RootElement);
        Assert.Single(toolCalls);
        Assert.Equal("directory_read", toolCalls[0].ToolName);
        Assert.Equal("/src", toolCalls[0].Arguments["path"]);

        // --- Turn 2: send tool result back to LLM ---
        var rawToolCallsJson = firstDoc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("tool_calls")
            .GetRawText();

        var secondMessages = new[]
        {
            LlmMessage.User("List files in /src"),
            new LlmMessage
            {
                Role = "assistant",
                Content = null!,
                RawToolCalls = rawToolCallsJson
            },
            new LlmMessage
            {
                Role = "tool",
                Content = "file1.cs\nfile2.cs\nProgram.cs",
                ToolCallId = "call_123"
            }
        };

        var secondResponse = await provider.ChatAsync(secondMessages, config, TestContext.Current.CancellationToken);

        // Assert: second call should include tool result in messages
        Assert.Equal(2, handler.AllCapturedRequestBodies.Count);
        var secondRequestBody = handler.AllCapturedRequestBodies[1];
        using var secondDoc = JsonDocument.Parse(secondRequestBody);
        var messagesArray = secondDoc.RootElement.GetProperty("messages");

        // Verify tool message is present with tool_call_id
        bool hasToolMessage = false;
        foreach (var msg in messagesArray.EnumerateArray())
        {
            if (msg.TryGetProperty("role", out var role) && role.GetString() == "tool")
            {
                hasToolMessage = true;
                Assert.Equal("call_123", msg.GetProperty("tool_call_id").GetString());
                Assert.Contains("file1.cs", msg.GetProperty("content").GetString()!);
            }
        }
        Assert.True(hasToolMessage, "Second request should contain a tool-role message with results");

        // Verify the final text response
        Assert.Equal("Hello!", secondResponse.Content);
    }

    #endregion

    #region Test 3: ParsesToolCallsFromResponse

    [Fact]
    public async Task ParsesToolCallsFromResponse_MultipleToolCalls()
    {
        // Arrange
        using var handler = new MockHttpMessageHandler();
        handler.EnqueueResponse(MultipleToolCallsResponse);

        var factory = CreateMockFactory(handler);
        var strategy = CreateStrategy();
        var config = CreateConfig(tools: new List<ToolSchema> { SampleToolSchema, SecondToolSchema });

        using var provider = new OpenAIProvider(config, factory, strategy);

        // Act
        var response = await provider.GenerateAsync("Read files in /src", config, TestContext.Current.CancellationToken);

        // Assert: RawResponseBody is populated when strategy is active
        Assert.NotNull(response.RawResponseBody);

        // Parse tool calls via the parser
        using var doc = JsonDocument.Parse(response.RawResponseBody);
        var toolCalls = strategy.Parser.ParseToolCalls(doc.RootElement);

        Assert.Equal(2, toolCalls.Count);

        // First tool call: directory_read
        Assert.Equal("call_A1", toolCalls[0].Id);
        Assert.Equal("directory_read", toolCalls[0].ToolName);
        Assert.Equal("/src", toolCalls[0].Arguments["path"]);

        // Second tool call: file_read
        Assert.Equal("call_B2", toolCalls[1].Id);
        Assert.Equal("file_read", toolCalls[1].ToolName);
        Assert.Equal("/src/main.cs", toolCalls[1].Arguments["file_path"]);
        Assert.Equal("utf-8", toolCalls[1].Arguments["encoding"]);
    }

    #endregion

    #region Test 4: FallbackToText_WhenNoToolCallingStrategy

    [Fact]
    public async Task FallbackToText_WhenNoToolCallingStrategy()
    {
        // Arrange
        using var handler = new MockHttpMessageHandler();
        handler.EnqueueResponse(TextResponse);

        var factory = CreateMockFactory(handler);
        // No tool calling strategy — use the constructor without strategy
        var config = CreateConfig(tools: new List<ToolSchema> { SampleToolSchema });

        using var provider = new OpenAIProvider(config, factory, logger: null);

        // Act
        var response = await provider.GenerateAsync("Hello", config, TestContext.Current.CancellationToken);

        // Assert: payload should NOT contain "tools"
        Assert.NotNull(handler.CapturedRequestBody);
        using var doc = JsonDocument.Parse(handler.CapturedRequestBody);
        var root = doc.RootElement;

        Assert.False(root.TryGetProperty("tools", out _),
            "Payload should NOT contain 'tools' when no strategy is set");
        Assert.False(root.TryGetProperty("tool_choice", out _),
            "Payload should NOT contain 'tool_choice' when no strategy is set");

        // RawResponseBody should be null without a strategy
        Assert.Null(response.RawResponseBody);

        // Content should still be parsed normally
        Assert.Equal("Hello!", response.Content);
    }

    #endregion
}
