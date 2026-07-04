using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.ToolCalling;
using Orkeon.Infrastructure.Tests.TestDoubles;

namespace Orkeon.Infrastructure.Tests.LLMs.ToolCalling;

/// <summary>
/// R10.7 — verifies that providers built by <see cref="LlmProviderFactory"/> carry a native
/// tool calling strategy when their API speaks the OpenAI dialect:
/// <list type="bullet">
/// <item>Azure OpenAI is wired with the OpenAI strategy: the chat payload carries
/// <c>tools</c>/<c>tool_choice</c> and the response exposes <c>RawResponseBody</c> so
/// <c>tool_calls</c> can be parsed natively (these tests fail on the pre-R10.7 wiring,
/// where the factory built the provider without a strategy).</item>
/// <item>Ollama intentionally keeps the text fallback: the provider targets the
/// prompt-completion API (<c>/api/generate</c>), whose pipeline is not OpenAI
/// tools-compatible — see <c>docs/reference/limitations.md</c>.</item>
/// </list>
/// </summary>
public class FactoryNativeToolCallingTests
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

    private const string OpenAiTextResponse = """
        {"choices":[{"message":{"role":"assistant","content":"Hello!"}}],"usage":{"total_tokens":10}}
        """;

    private const string OpenAiSingleToolCallResponse = """
        {"choices":[{"message":{"role":"assistant","content":null,"tool_calls":[{"id":"call_123","type":"function","function":{"name":"directory_read","arguments":"{\"path\":\"/src\"}"}}]}}],"usage":{"total_tokens":15}}
        """;

    private const string OllamaGenerateResponse = """
        {"response":"pong","done":true,"total_duration":100,"eval_duration":50}
        """;

#pragma warning disable CS0618 // LlmConfig.ApiKey is obsolete but still the factory-facing surface
    private static LlmConfig CreateAzureConfig(IReadOnlyList<ToolSchema>? tools = null)
    {
        return LlmConfig.Create("gpt-4", "test-api-key-fake") with
        {
            BaseUrl = new Uri("https://myinstance.openai.azure.com"),
            Tools = tools,
            ToolMode = ToolCallMode.Auto,
            TimeoutSeconds = 10
        };
    }

    private static LlmConfig CreateOllamaConfig(IReadOnlyList<ToolSchema>? tools = null)
    {
        return LlmConfig.Create("llama3") with
        {
            BaseUrl = new Uri("http://localhost:11434"),
            Tools = tools,
            ToolMode = ToolCallMode.Auto,
            TimeoutSeconds = 10
        };
    }
#pragma warning restore CS0618

    private static LlmProviderFactory CreateFactory(TestHttpClientFactory httpClientFactory)
        => new(httpClientFactory, NullLoggerFactory.Instance);

    private static async Task<(Uri Uri, string Body)> ReadSingleCapturedRequestAsync(TestHttpMessageHandler handler)
    {
        var request = Assert.Single(handler.CapturedRequests);
        Assert.NotNull(request.RequestUri);
        Assert.NotNull(request.Content);
        var body = await request.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return (request.RequestUri, body);
    }

    #endregion

    #region Azure OpenAI — native strategy wired by the factory (R10.7)

    [Fact]
    public async Task AzureProviderFromFactory_ChatAsyncWithTools_InjectsNativeToolsIntoPayload()
    {
        // Arrange — provider built by the factory (not constructed directly)
        var httpClientFactory = new TestHttpClientFactory();
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, OpenAiTextResponse);
        using var httpClient = new HttpClient(handler);
        httpClientFactory.RegisterClient("AzureOpenAILlmProvider", httpClient);

        var config = CreateAzureConfig(tools: new List<ToolSchema> { SampleToolSchema });
        var adapter = Assert.IsType<LlmProviderAdapter>(CreateFactory(httpClientFactory).Create("azure-openai", config));
        using var provider = Assert.IsType<AzureOpenAILlmProvider>(adapter.UnderlyingProvider);

        // Multi-turn conversation with tool metadata (OpenAI dialect replay)
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

        // Assert — native tools payload (fails on pre-R10.7 wiring: text-flattened prompt, no tools)
        var (uri, body) = await ReadSingleCapturedRequestAsync(handler);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("tools", out var toolsArray),
            "Factory-built Azure provider should inject the 'tools' array (native strategy)");
        Assert.Equal(1, toolsArray.GetArrayLength());
        Assert.Equal("directory_read",
            toolsArray[0].GetProperty("function").GetProperty("name").GetString());

        Assert.True(root.TryGetProperty("tool_choice", out var toolChoice),
            "Factory-built Azure provider should inject 'tool_choice'");
        Assert.Equal("auto", toolChoice.GetString());

        // Tool-role replay is preserved in the OpenAI dialect
        var hasToolMessage = root.GetProperty("messages").EnumerateArray().Any(m =>
            m.TryGetProperty("role", out var role) && role.GetString() == "tool" &&
            m.GetProperty("tool_call_id").GetString() == "call_prev");
        Assert.True(hasToolMessage, "Tool-role message with tool_call_id should be serialized");

        // The Azure deployment-based endpoint is preserved in the native path
        Assert.Contains("/openai/deployments/gpt-4/chat/completions", uri.AbsoluteUri, StringComparison.Ordinal);
        Assert.Contains("api-version=", uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AzureProviderFromFactory_ResponsePipeline_MapsNativeToolCalls()
    {
        // Arrange
        var httpClientFactory = new TestHttpClientFactory();
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, OpenAiSingleToolCallResponse);
        using var httpClient = new HttpClient(handler);
        httpClientFactory.RegisterClient("AzureOpenAILlmProvider", httpClient);

        var config = CreateAzureConfig(tools: new List<ToolSchema> { SampleToolSchema });
        var adapter = Assert.IsType<LlmProviderAdapter>(CreateFactory(httpClientFactory).Create("azure", config));
        using var provider = Assert.IsType<AzureOpenAILlmProvider>(adapter.UnderlyingProvider);

        // Act
        var response = await provider.ChatAsync(
            new[] { LlmMessage.User("List files in /src") }, config, TestContext.Current.CancellationToken);

        // Assert — RawResponseBody is only populated with a native strategy
        // (null on pre-R10.7 wiring, which forced the text fallback path in the agent loop)
        Assert.NotNull(response.RawResponseBody);

        var strategy = new OpenAIToolCallingStrategy(NullLogger<OpenAIToolCallParser>.Instance);
        Assert.True(strategy.SupportsNativeToolCalling);

        using var doc = JsonDocument.Parse(response.RawResponseBody);
        var toolCalls = strategy.Parser.ParseToolCalls(doc.RootElement);
        var call = Assert.Single(toolCalls);
        Assert.Equal("call_123", call.Id);
        Assert.Equal("directory_read", call.ToolName);
        Assert.Equal("/src", call.Arguments["path"]);
    }

    [Fact]
    public async Task AzureProviderFromFactory_WithoutTools_DoesNotInjectToolsPayload()
    {
        // Arrange — no tools configured: crews without tools must keep their behavior
        var httpClientFactory = new TestHttpClientFactory();
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, OpenAiTextResponse);
        using var httpClient = new HttpClient(handler);
        httpClientFactory.RegisterClient("AzureOpenAILlmProvider", httpClient);

        var config = CreateAzureConfig(tools: null);
        var adapter = Assert.IsType<LlmProviderAdapter>(CreateFactory(httpClientFactory).Create("azure-openai", config));
        using var provider = Assert.IsType<AzureOpenAILlmProvider>(adapter.UnderlyingProvider);

        // Act
        var response = await provider.ChatAsync(
            new[] { LlmMessage.User("Say hello") }, config, TestContext.Current.CancellationToken);

        // Assert — plain text behavior preserved, no tool keys in the payload
        Assert.Equal("Hello!", response.Content);

        var (_, body) = await ReadSingleCapturedRequestAsync(handler);
        using var doc = JsonDocument.Parse(body);
        Assert.False(doc.RootElement.TryGetProperty("tools", out _),
            "No 'tools' should be sent when the config has no tool schemas");
        Assert.False(doc.RootElement.TryGetProperty("tool_choice", out _),
            "No 'tool_choice' should be sent when the config has no tool schemas");
    }

    #endregion

    #region Ollama — documented statu quo: text fallback (R10.7 decision)

    [Fact]
    public async Task OllamaProviderFromFactory_KeepsTextFallback_NoNativeToolInjection()
    {
        // Arrange — even with tool schemas configured, the Ollama provider goes through
        // /api/generate (prompt completion) and must not pretend to support native tools.
        var httpClientFactory = new TestHttpClientFactory();
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, OllamaGenerateResponse);
        using var httpClient = new HttpClient(handler);
        httpClientFactory.RegisterClient("OllamaLlmProvider", httpClient);

        var config = CreateOllamaConfig(tools: new List<ToolSchema> { SampleToolSchema });
        var adapter = Assert.IsType<LlmProviderAdapter>(CreateFactory(httpClientFactory).Create("ollama", config));
        using var provider = Assert.IsType<OllamaLlmProvider>(adapter.UnderlyingProvider);

        // Act
        var response = await provider.ChatAsync(
            new[] { LlmMessage.User("ping") }, config, TestContext.Current.CancellationToken);

        // Assert — text pipeline untouched: completion endpoint, no tools key, no raw body
        Assert.Equal("pong", response.Content);
        Assert.Null(response.RawResponseBody);

        var (uri, body) = await ReadSingleCapturedRequestAsync(handler);
        Assert.EndsWith("/api/generate", uri.AbsolutePath, StringComparison.Ordinal);

        using var doc = JsonDocument.Parse(body);
        Assert.False(doc.RootElement.TryGetProperty("tools", out _),
            "Ollama provider must not inject 'tools' into the /api/generate payload (text fallback by design)");
    }

    #endregion
}
