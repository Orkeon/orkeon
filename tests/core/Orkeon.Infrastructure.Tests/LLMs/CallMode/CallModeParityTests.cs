using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.CallMode;
using Orkeon.Infrastructure.LLMs.ToolCalling;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.LLMs.CallMode;

/// <summary>
/// The same answer served once as the buffered body and once as the stream gives the same text,
/// tool call, finish reason and usage through the decorator, and Auto goes on the wire streamed.
/// </summary>
public sealed class CallModeParityTests
{
    private static readonly LlmMessage[] Conversation = [LlmMessage.User("weather in Lyon?")];

    public static TheoryData<string> Vendors => ["openai", "anthropic", "ollama"];

    private const string OpenAiBuffered =
        """{"choices":[{"message":{"role":"assistant","content":"Checking.","tool_calls":[{"id":"c1","type":"function","function":{"name":"get_weather","arguments":"{\"city\":\"Lyon\"}"}}]},"finish_reason":"tool_calls"}],"usage":{"prompt_tokens":12,"completion_tokens":9,"total_tokens":21}}""";

    private static readonly string OpenAiStream = string.Join("\n\n",
        """data: {"choices":[{"delta":{"content":"Checking."}}]}""",
        """data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"c1","type":"function","function":{"name":"get_weather","arguments":"{\"city\":"}}]}}]}""",
        """data: {"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"\"Lyon\"}"}}]}}]}""",
        """data: {"choices":[{"delta":{},"finish_reason":"tool_calls"}]}""",
        """data: {"choices":[],"usage":{"prompt_tokens":12,"completion_tokens":9,"total_tokens":21}}""",
        "data: [DONE]",
        "") + "\n";

    private const string AnthropicBuffered =
        """{"content":[{"type":"text","text":"Checking."},{"type":"tool_use","id":"toolu_01","name":"get_weather","input":{"city":"Lyon"}}],"stop_reason":"tool_use","usage":{"input_tokens":12,"output_tokens":9}}""";

    private static readonly string AnthropicStream = string.Join("\n\n",
        """data: {"type":"message_start","message":{"usage":{"input_tokens":12}}}""",
        """data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}""",
        """data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Checking."}}""",
        """data: {"type":"content_block_stop","index":0}""",
        """data: {"type":"content_block_start","index":1,"content_block":{"type":"tool_use","id":"toolu_01","name":"get_weather","input":{}}}""",
        """data: {"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"{\"city\": "}}""",
        """data: {"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"\"Lyon\"}"}}""",
        """data: {"type":"content_block_stop","index":1}""",
        """data: {"type":"message_delta","delta":{"stop_reason":"tool_use"},"usage":{"output_tokens":9}}""",
        """data: {"type":"message_stop"}""",
        "") + "\n";

    private const string OllamaBuffered =
        """{"message":{"role":"assistant","content":"Checking.","tool_calls":[{"function":{"name":"get_weather","arguments":{"city":"Lyon"}}}]},"done":true,"done_reason":"stop","prompt_eval_count":12,"eval_count":9}""";

    private static readonly string OllamaStream = string.Join('\n',
        """{"message":{"role":"assistant","content":"Checking.","tool_calls":[{"function":{"name":"get_weather","arguments":{"city":"Lyon"}}}]},"done":false}""",
        """{"message":{"role":"assistant","content":""},"done":true,"done_reason":"stop","prompt_eval_count":12,"eval_count":9}""");

    private static LlmConfig WithTools(LlmConfig config) => config with
    {
        Tools = [new ToolSchema("get_weather", "Get the weather", [])],
    };

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The factory and its clients live as long as the provider the test returns.")]
    private static (ILlmProvider Provider, MockHttpMessageHandler Handler) Build(string vendor, bool streamed)
    {
        var factory = new MockHttpClientFactory();
        switch (vendor)
        {
            case "openai":
            {
                var handler = factory.SetupDefaultHandler();
                Serve(handler, streamed ? OpenAiStream : OpenAiBuffered, streamed ? "text/event-stream" : "application/json");
                var config = WithTools(LlmConfig.Create("test-model") with { MaxRetries = 0, ApiKey = "sk-test" });
#pragma warning disable CS0618
                var provider = new OpenAIProvider(config, factory,
                    new OpenAIToolCallingStrategy(NullLogger<OpenAIToolCallParser>.Instance), NullLogger<OpenAIProvider>.Instance);
#pragma warning restore CS0618
                return (provider, handler);
            }
            case "anthropic":
            {
                var handler = factory.AddClientWithHandler("AnthropicLlmProvider");
                Serve(handler, streamed ? AnthropicStream : AnthropicBuffered, streamed ? "text/event-stream" : "application/json");
                var config = WithTools(LlmConfig.Create("claude-sonnet-5", "sk-ant"));
                return (new AnthropicLlmProvider(config, factory, new AnthropicToolCallingStrategy(), NullLogger<AnthropicLlmProvider>.Instance), handler);
            }
            default:
            {
                var handler = factory.AddClientWithHandler("OllamaLlmProvider");
                Serve(handler, streamed ? OllamaStream : OllamaBuffered, "application/json");
                var config = WithTools(LlmConfig.Create("llama3.2") with { BaseUrl = new Uri("http://localhost:11434") });
                return (new OllamaLlmProvider(config, factory,
                    new OpenAIToolCallingStrategy(NullLogger<OpenAIToolCallParser>.Instance), NullLogger<OllamaLlmProvider>.Instance), handler);
            }
        }
    }

    private static void Serve(MockHttpMessageHandler handler, string body, string mediaType) =>
        handler.SetResponseFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, mediaType),
        });

    private static List<string> ToolCalls(LlmResponse response)
    {
        var calls = new List<string>();
        if (response.RawResponseBody is null)
            return calls;

        using var doc = JsonDocument.Parse(response.RawResponseBody);
        var root = doc.RootElement;
        if (root.TryGetProperty("content", out var blocks) && blocks.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in blocks.EnumerateArray().Where(b => b.TryGetProperty("type", out var t) && t.GetString() == "tool_use"))
                calls.Add($"{block.GetProperty("name").GetString()}|{Canonical(block.GetProperty("input"))}");
            return calls;
        }

        var message = root.TryGetProperty("choices", out var choices) ? choices[0].GetProperty("message") : root.GetProperty("message");
        if (message.TryGetProperty("tool_calls", out var toolCalls))
        {
            foreach (var call in toolCalls.EnumerateArray())
            {
                var function = call.GetProperty("function");
                calls.Add($"{function.GetProperty("name").GetString()}|{Canonical(function.GetProperty("arguments"))}");
            }
        }

        return calls;
    }

    private static string Canonical(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.String)
            return JsonSerializer.Serialize(arguments);

        using var parsed = JsonDocument.Parse(arguments.GetString()!);
        return JsonSerializer.Serialize(parsed.RootElement);
    }

    private static string? FinishReason(LlmResponse response)
    {
        var value = response.Metadata
            .Where(m => m.Key.Contains("finish_reason", StringComparison.OrdinalIgnoreCase)
                || m.Key.Contains("stop_reason", StringComparison.OrdinalIgnoreCase)
                || m.Key.Contains("done_reason", StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Value?.ToString())
            .FirstOrDefault();
        return value switch
        {
            "tool_use" => "tool_calls",
            "end_turn" => "stop",
            _ => value,
        };
    }

    [Theory]
    [MemberData(nameof(Vendors))]
    [Trait("BR", "BR-11")]
    public async Task ShouldReturnTheSameTextToolCallsAndUsage_WhenStreamedOrBuffered(string vendor)
    {
        var ct = TestContext.Current.CancellationToken;
        var (bufferedInner, bufferedHandler) = Build(vendor, streamed: false);
        var (streamedInner, streamedHandler) = Build(vendor, streamed: true);
        var buffered = new CallModeLlmProvider(bufferedInner, LlmCallMode.Buffer, NullLogger.Instance);
        var auto = new CallModeLlmProvider(streamedInner, LlmCallMode.Auto, NullLogger.Instance);

        var whole = await buffered.ChatAsync(Conversation, cancellationToken: ct);
        var folded = await auto.ChatAsync(Conversation, cancellationToken: ct);

        Assert.Equal("Checking.", whole.Content);
        Assert.Equal(whole.Content, folded.Content);
        Assert.Equal(["get_weather|{\"city\":\"Lyon\"}"], ToolCalls(whole));
        Assert.Equal(ToolCalls(whole), ToolCalls(folded));
        Assert.Equal(FinishReason(whole), FinishReason(folded));
        Assert.Equal(12, whole.PromptTokens);
        Assert.Equal(whole.PromptTokens, folded.PromptTokens);
        Assert.Equal(whole.CompletionTokens, folded.CompletionTokens);
        Assert.Equal(whole.TokensUsed, folded.TokensUsed);

        Assert.False(RequestsStream(bufferedHandler));
        Assert.True(RequestsStream(streamedHandler));
        Assert.Equal(LlmWireMode.Buffer, buffered.LastWireMode);
        Assert.Equal(LlmWireMode.Stream, auto.LastWireMode);
    }

    private static bool RequestsStream(MockHttpMessageHandler handler)
    {
        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        return doc.RootElement.TryGetProperty("stream", out var stream) && stream.ValueKind == JsonValueKind.True;
    }
}
