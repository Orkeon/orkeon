using System.Net;
using System.Text;
using System.Text.Json;
using Orkeon.Application.Crew.Execution;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.ToolCalling;
using Orkeon.Infrastructure.Tests.TestDoubles;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// R-08 / R-46 - an assistant tool-call turn replayed to Anthropic keeps its signed thinking
/// blocks unchanged and first, and a malformed tool call in a replayed turn is refused by name.
/// </summary>
public class AnthropicThinkingReplayTests
{
    private const string ThinkingText = "Let me look at the directory first.";
    private const string Signature = "EuYBCkQYAiJA-signature-opaque==";
    private const string RedactedData = "EmwKAhgBEgy3va3pzix/LafPsn4aDFIT2Xlxh0L5L8rLVyIwxtE3rAFBa8cr3qpP";

    private static readonly LlmConfig DefaultConfig = LlmConfig.Create("claude-haiku-5-5", "test-key");

    private static LlmConfig HttpConfig() => LlmConfig.Create("claude-haiku-5-5", "test-key") with
    {
        BaseUrl = new Uri("https://api.anthropic.com"),
        TimeoutSeconds = 10
    };

    private static AnthropicLlmProvider NewProvider(TestHttpMessageHandler handler, LlmConfig config)
    {
        var factory = new TestHttpClientFactory();
        factory.RegisterClient("AnthropicLlmProvider", new HttpClient(handler));
        return new AnthropicLlmProvider(config, factory, new AnthropicToolCallingStrategy());
    }

    private static TestHttpMessageHandler Respond(string body, string mediaType) =>
        new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, mediaType)
        });

    private static LlmMessage[] ReplayConversation(LlmResponse response)
    {
        Assert.NotNull(response.RawResponseBody);
        using var bodyDoc = JsonDocument.Parse(response.RawResponseBody);
        var turn = NativeToolCallTranscript.AssistantTurn(response, bodyDoc.RootElement);
        return
        [
            LlmMessage.User("List /workspace"),
            turn,
            new LlmMessage { Role = "tool", Content = "a.cs", ToolCallId = "toolu_1" },
        ];
    }

    private static JsonElement ReplayedAssistantContent(LlmMessage[] messages)
    {
        var (conversation, _) = AnthropicLlmProvider.SeparateSystemMessages(messages, DefaultConfig);
        var json = JsonSerializer.Serialize(conversation[1]);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("assistant", doc.RootElement.GetProperty("role").GetString());
        return doc.RootElement.GetProperty("content").Clone();
    }

    [Fact]
    [Trait("BR", "R-08")]
    public async Task ShouldReplaySignedThinkingFirst_WhenBufferedTurnHadThinkingAndToolUse()
    {
        // Arrange
        var body = $$$"""
            {"content":[{"type":"thinking","thinking":"{{{ThinkingText}}}","signature":"{{{Signature}}}"},
            {"type":"text","text":"Reading."},
            {"type":"tool_use","id":"toolu_1","name":"dir_read","input":{"path":"/workspace"}}],
            "stop_reason":"tool_use","usage":{"input_tokens":5,"output_tokens":7}}
            """;
        using var handler = Respond(body, "application/json");
        using var provider = NewProvider(handler, HttpConfig());
        var response = await provider.ChatAsync([LlmMessage.User("List /workspace")], HttpConfig(),
            TestContext.Current.CancellationToken);

        // Act
        var content = ReplayedAssistantContent(ReplayConversation(response));

        // Assert
        var blocks = content.EnumerateArray().ToArray();
        Assert.Equal("thinking", blocks[0].GetProperty("type").GetString());
        Assert.Equal(ThinkingText, blocks[0].GetProperty("thinking").GetString());
        Assert.Equal(Signature, blocks[0].GetProperty("signature").GetString());
        Assert.Equal("tool_use", blocks[^1].GetProperty("type").GetString());
        Assert.Equal("toolu_1", blocks[^1].GetProperty("id").GetString());
    }

    [Fact]
    [Trait("BR", "R-08")]
    public async Task ShouldReplaySignedThinkingFirst_WhenStreamedTurnHadSignatureDelta()
    {
        // Arrange
        const string sse = """
            data: {"type":"message_start","message":{"usage":{"input_tokens":5,"output_tokens":1}}}

            data: {"type":"content_block_start","index":0,"content_block":{"type":"thinking","thinking":""}}

            data: {"type":"content_block_delta","index":0,"delta":{"type":"thinking_delta","thinking":"Let me look at "}}

            data: {"type":"content_block_delta","index":0,"delta":{"type":"thinking_delta","thinking":"the directory first."}}

            data: {"type":"content_block_delta","index":0,"delta":{"type":"signature_delta","signature":"EuYBCkQYAiJA-signature-opaque=="}}

            data: {"type":"content_block_stop","index":0}

            data: {"type":"content_block_start","index":1,"content_block":{"type":"tool_use","id":"toolu_1","name":"dir_read","input":{}}}

            data: {"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"{\"path\":\"/workspace\"}"}}

            data: {"type":"content_block_stop","index":1}

            data: {"type":"message_delta","delta":{"stop_reason":"tool_use"},"usage":{"output_tokens":7}}

            data: {"type":"message_stop"}


            """;
        using var handler = Respond(sse, "text/event-stream");
        using var provider = NewProvider(handler, HttpConfig());
        LlmResponse? final = null;
        await foreach (var ev in provider.ChatStreamingAsync([LlmMessage.User("List /workspace")], HttpConfig(),
                           TestContext.Current.CancellationToken))
        {
            if (ev.Kind == LlmStreamEventKind.Completed)
                final = ev.FinalResponse;
        }

        // Act
        Assert.NotNull(final);
        var content = ReplayedAssistantContent(ReplayConversation(final));

        // Assert
        var blocks = content.EnumerateArray().ToArray();
        Assert.Equal("thinking", blocks[0].GetProperty("type").GetString());
        Assert.Equal(ThinkingText, blocks[0].GetProperty("thinking").GetString());
        Assert.Equal(Signature, blocks[0].GetProperty("signature").GetString());
        Assert.Equal("tool_use", blocks[^1].GetProperty("type").GetString());
    }

    [Fact]
    [Trait("BR", "R-08")]
    public async Task ShouldReplayRedactedThinkingUnchanged_WhenTurnHadRedactedBlock()
    {
        // Arrange - received order: signed thinking, redacted, then tool_use
        var body = $$$"""
            {"content":[{"type":"thinking","thinking":"{{{ThinkingText}}}","signature":"{{{Signature}}}"},
            {"type":"redacted_thinking","data":"{{{RedactedData}}}"},
            {"type":"tool_use","id":"toolu_1","name":"dir_read","input":{"path":"/workspace"}}],
            "stop_reason":"tool_use","usage":{"input_tokens":5,"output_tokens":7}}
            """;
        using var handler = Respond(body, "application/json");
        using var provider = NewProvider(handler, HttpConfig());
        var response = await provider.ChatAsync([LlmMessage.User("List /workspace")], HttpConfig(),
            TestContext.Current.CancellationToken);

        // Act
        var content = ReplayedAssistantContent(ReplayConversation(response));

        // Assert
        var blocks = content.EnumerateArray().ToArray();
        Assert.Equal(3, blocks.Length);
        Assert.Equal("thinking", blocks[0].GetProperty("type").GetString());
        Assert.Equal(Signature, blocks[0].GetProperty("signature").GetString());
        Assert.Equal("redacted_thinking", blocks[1].GetProperty("type").GetString());
        Assert.Equal(RedactedData, blocks[1].GetProperty("data").GetString());
        Assert.Equal("tool_use", blocks[2].GetProperty("type").GetString());
    }

    [Fact]
    [Trait("BR", "R-08")]
    [Trait("Category", "Integration")]
    public async Task ShouldReplayThinkingAgainstRealApi_WhenKeyIsSet()
    {
        // Arrange
        var envValue = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        Assert.SkipWhen(string.IsNullOrEmpty(envValue), "ANTHROPIC_API_KEY is not set");
        var tool = new ToolSchema(
            Name: "dir_read",
            Description: "Lists the files of a directory",
            Parameters: new Dictionary<string, ParameterSchema>
            {
                ["path"] = new ParameterSchema(Type: "string", Description: "The directory path", Required: true)
            });
        var config = LlmConfig.Create("claude-haiku-5-5", envValue!) with
        {
            BaseUrl = new Uri("https://api.anthropic.com"),
            Tools = new List<ToolSchema> { tool },
            ToolMode = ToolCallMode.Auto,
            TimeoutSeconds = 120,
            Thinking = new LlmThinkingConfig { Enabled = true }
        };
        using var provider = new AnthropicLlmProvider(config, new TestHttpClientFactory(), new AnthropicToolCallingStrategy());
        var ct = TestContext.Current.CancellationToken;
        const string prompt = "Use the dir_read tool on /workspace, then tell me what you found.";
        var first = await provider.ChatAsync([LlmMessage.User(prompt)], config, ct);
        Assert.False(first.Metadata?.ContainsKey("error") ?? false);
        Assert.NotNull(first.RawResponseBody);
        using var doc = JsonDocument.Parse(first.RawResponseBody);
        var id = doc.RootElement.GetProperty("content").EnumerateArray()
            .First(b => b.GetProperty("type").GetString() == "tool_use").GetProperty("id").GetString();

        // Act
        var second = await provider.ChatAsync(
        [
            LlmMessage.User(prompt),
            NativeToolCallTranscript.AssistantTurn(first, doc.RootElement),
            new LlmMessage { Role = "tool", Content = "a.cs b.cs", ToolCallId = id },
        ], config, ct);

        // Assert
        Assert.False(second.Metadata?.ContainsKey("error") ?? false);
        Assert.False(string.IsNullOrEmpty(second.Content));
    }

    [Fact]
    [Trait("BR", "R-46")]
    public void ShouldRefuseReplay_WhenToolCallHasNoId()
    {
        // Arrange - the second call (index 1) has no id
        var messages = new[]
        {
            LlmMessage.User("Task"),
            new LlmMessage
            {
                Role = "assistant",
                Content = "",
                RawToolCalls = """[{"type":"tool_use","id":"toolu_1","name":"dir_read","input":{}},{"type":"tool_use","name":"grep","input":{}}]"""
            },
            new LlmMessage { Role = "tool", Content = "x", ToolCallId = "toolu_1" },
        };

        // Act
        var ex = Assert.ThrowsAny<Exception>(() => AnthropicLlmProvider.SeparateSystemMessages(messages, DefaultConfig));

        // Assert - structured, not a bare KeyNotFoundException, naming index, name and field
        Assert.IsNotType<KeyNotFoundException>(ex);
        Assert.Contains("1", ex.Message);
        Assert.Contains("grep", ex.Message);
        Assert.Contains("'id'", ex.Message);
    }

    [Fact]
    [Trait("BR", "R-46")]
    public void ShouldRefuseReplay_WhenToolCallArgumentsAreNotAnObject()
    {
        // Arrange - OpenAI shape whose arguments string parses to an array
        var messages = new[]
        {
            LlmMessage.User("Task"),
            new LlmMessage
            {
                Role = "assistant",
                Content = "",
                RawToolCalls = """[{"id":"call_1","function":{"name":"grep","arguments":"[1]"}}]"""
            },
            new LlmMessage { Role = "tool", Content = "x", ToolCallId = "call_1" },
        };

        // Act
        var ex = Assert.ThrowsAny<Exception>(() => AnthropicLlmProvider.SeparateSystemMessages(messages, DefaultConfig));

        // Assert
        Assert.Contains("0", ex.Message);
        Assert.Contains("grep", ex.Message);
        Assert.Contains("'arguments'", ex.Message);
    }
}
