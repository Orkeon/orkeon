using System.Net;
using System.Text.Json;
using Polly;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools.Protocol;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs.Base;

/// <summary>
/// Placement of <see cref="LlmConfig.SystemMessage"/> on the native chat path of the
/// OpenAI-compatible providers.
/// </summary>
/// <remarks>
/// <para>
/// A plain conversation is flattened into a single prompt and goes out through
/// <c>GenerateAsync</c>, which prepends the configured system message. The native chat path —
/// taken as soon as the config declares tools, the messages carry tool metadata, or an image
/// is attached — built its messages array from the passed messages alone and dropped it.
/// </para>
/// <para>
/// That makes the defect conditional, which is worse than an outright omission: a crew works,
/// then the operator gives its agent a tool and the system prompt silently vanishes. Ten of
/// the twelve providers were affected; Anthropic and Ollama were not.
/// </para>
/// <para>
/// Roughly 400 mocked tests cover these providers and none caught it — they assert on the
/// response, never on what goes out on the wire. These read the outgoing payload, which is
/// the only place the defect was ever visible.
/// </para>
/// </remarks>
public class OpenAICompatibleProviderBaseSystemMessageTests
{
    private const string ConfiguredSystem = "Always end your reply with the token ORKEON_OK.";
    private const string ConversationSystem = "You are a pirate.";

    private readonly TestDoubles.TestHttpClientFactory _httpClientFactory = new();
    private readonly IAsyncPolicy<HttpResponseMessage> _noOpPolicy = Policy.NoOpAsync<HttpResponseMessage>();

    private static readonly string OkResponse = JsonSerializer.Serialize(new
    {
        choices = new[] { new { message = new { content = "ok" } } },
        usage = new { total_tokens = 1 },
    });

    /// <summary>
    /// A configuration on the native chat path. Declaring a tool is what selects it — the same
    /// trigger a real agent hits the moment it is given one.
    /// </summary>
    private static LlmConfig NativeChatConfig() =>
        LlmConfig.Create(TestModelName, TestApiKey) with
        {
            Tools =
            [
                new ToolSchema(
                    "probe_tool",
                    "A tool, present only to select the native chat path.",
                    new Dictionary<string, ParameterSchema>
                    {
                        ["city"] = new("string", "A city.", Required: true),
                    }),
            ],
        };

    /// <summary>Runs one chat call and returns the <c>messages</c> array actually sent.</summary>
    private async Task<JsonElement> CaptureSentMessagesAsync(LlmConfig config, LlmMessage[] messages)
    {
        using var handler = TestDoubles.TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, OkResponse);
        using var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("TestableOpenAICompatibleProvider", httpClient);

        using var provider = new TestableOpenAICompatibleProvider(config, _httpClientFactory, _noOpPolicy);
        await provider.ChatAsync(messages, cancellationToken: TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.CapturedRequests);
        Assert.NotNull(request.Content);
        var body = await request.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("messages").Clone();
    }

    private static (string Role, string Content) At(JsonElement messages, int index) =>
        (messages[index].GetProperty("role").GetString() ?? "",
         messages[index].GetProperty("content").GetString() ?? "");

    [Fact]
    public async Task ShouldPrependTheConfiguredSystemMessage_WhenTheConversationHasNone()
    {
        var config = NativeChatConfig() with { SystemMessage = ConfiguredSystem };

        var messages = await CaptureSentMessagesAsync(config, [LlmMessage.User("What is 2 + 2?")]);

        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal(("system", ConfiguredSystem), At(messages, 0));
        Assert.Equal(("user", "What is 2 + 2?"), At(messages, 1));
    }

    /// <summary>
    /// The system message must lead the array, not trail the history — position is what the
    /// vendor keys on.
    /// </summary>
    [Fact]
    public async Task ShouldPrependTheConfiguredSystemMessage_AheadOfAnExistingMultiTurnHistory()
    {
        var config = NativeChatConfig() with { SystemMessage = ConfiguredSystem };
        LlmMessage[] conversation =
        [
            LlmMessage.User("What is 2 + 2?"),
            LlmMessage.Assistant("4"),
            LlmMessage.User("And multiplied by 3?"),
        ];

        var messages = await CaptureSentMessagesAsync(config, conversation);

        Assert.Equal(4, messages.GetArrayLength());
        Assert.Equal(("system", ConfiguredSystem), At(messages, 0));
        Assert.Equal(("user", "What is 2 + 2?"), At(messages, 1));
        Assert.Equal(("assistant", "4"), At(messages, 2));
        Assert.Equal(("user", "And multiplied by 3?"), At(messages, 3));
    }

    /// <summary>
    /// Precedence matches <c>AnthropicLlmProvider.SeparateSystemMessages</c>: a system message
    /// carried by the conversation wins, and the configuration is only a fallback. The reverse
    /// would let ambient configuration overwrite a deliberate call-site instruction.
    /// </summary>
    [Fact]
    public async Task ShouldLeaveTheConversationSystemMessageInPlace_AndNotAddTheConfiguredOne()
    {
        var config = NativeChatConfig() with { SystemMessage = ConfiguredSystem };
        LlmMessage[] conversation =
        [
            LlmMessage.System(ConversationSystem),
            LlmMessage.User("Hello."),
        ];

        var messages = await CaptureSentMessagesAsync(config, conversation);

        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal(("system", ConversationSystem), At(messages, 0));
        Assert.DoesNotContain(ConfiguredSystem, messages.GetRawText(), StringComparison.Ordinal);
    }

    /// <summary>The untyped escape hatch resolves exactly like the typed property.</summary>
    [Fact]
    public async Task ShouldHonourTheSystemMessageCustomParameter_WhenTheTypedPropertyIsUnset()
    {
        var config = NativeChatConfig() with
        {
            CustomParameters = new Dictionary<string, object> { ["system_message"] = ConfiguredSystem },
        };

        var messages = await CaptureSentMessagesAsync(config, [LlmMessage.User("Hello.")]);

        Assert.Equal(("system", ConfiguredSystem), At(messages, 0));
    }

    [Fact]
    public async Task ShouldSendTheConversationUnchanged_WhenNoSystemMessageIsConfigured()
    {
        var messages = await CaptureSentMessagesAsync(NativeChatConfig(), [LlmMessage.User("Hello.")]);

        Assert.Equal(1, messages.GetArrayLength());
        Assert.Equal(("user", "Hello."), At(messages, 0));
    }

    /// <summary>Whitespace is not an instruction; sending it would waste a turn slot.</summary>
    [Fact]
    public async Task ShouldIgnoreABlankConfiguredSystemMessage()
    {
        var config = NativeChatConfig() with { SystemMessage = "   " };

        var messages = await CaptureSentMessagesAsync(config, [LlmMessage.User("Hello.")]);

        Assert.Equal(1, messages.GetArrayLength());
        Assert.Equal("user", At(messages, 0).Role);
    }

    /// <summary>
    /// The path a real agent takes on its second turn: replaying a tool result. It reaches the
    /// native chat builder through <c>HasToolMetadata</c> rather than through the tool schemas,
    /// so it is worth pinning separately.
    /// </summary>
    [Fact]
    public async Task ShouldPrependTheConfiguredSystemMessage_WhenReplayingAToolResult()
    {
        var config = LlmConfig.Create(TestModelName, TestApiKey) with { SystemMessage = ConfiguredSystem };
        LlmMessage[] conversation =
        [
            LlmMessage.User("What is the code?"),
            LlmMessage.Assistant("") with
            {
                RawToolCalls = """[{"id":"call-1","type":"function","function":{"name":"probe_tool","arguments":"{}"}}]""",
            },
            new LlmMessage { Role = "tool", ToolCallId = "call-1", Content = "ORKEON-4711" },
        ];

        var messages = await CaptureSentMessagesAsync(config, conversation);

        Assert.Equal(4, messages.GetArrayLength());
        Assert.Equal(("system", ConfiguredSystem), At(messages, 0));
        Assert.Equal("tool", messages[3].GetProperty("role").GetString());
    }

    /// <summary>
    /// The flattened path already prepended the system message before this fix; pinning it
    /// keeps the two paths from drifting apart again, which is how the defect arose.
    /// </summary>
    [Fact]
    public async Task ShouldAlsoSendTheSystemMessage_OnThePlainConversationPathWithNoTools()
    {
        var config = LlmConfig.Create(TestModelName, TestApiKey) with { SystemMessage = ConfiguredSystem };

        var messages = await CaptureSentMessagesAsync(config, [LlmMessage.User("Hello.")]);

        Assert.Equal(("system", ConfiguredSystem), At(messages, 0));
    }
}
