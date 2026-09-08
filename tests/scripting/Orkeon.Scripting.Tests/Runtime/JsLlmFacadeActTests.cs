using Jint;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Scripting.Runtime;
using ProtocolToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;

namespace Orkeon.Scripting.Tests.Runtime;

/// <summary>
/// <c>ctx.llm.act</c>'s real tool-calling loop. Drives a fake provider that returns a
/// tool call on the first turn and a final answer on the second, asserting the loop executes
/// the matching <see cref="IBaseTool"/> and feeds the result back.
/// </summary>
public sealed class JsLlmFacadeActTests
{
    private static string ToolCallBody(string name, string argsJson)
    {
        // OpenAI/DeepSeek chat response shape with one tool call; `arguments` is a JSON string.
        var argsEncoded = System.Text.Json.JsonSerializer.Serialize(argsJson);
        return "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"\",\"tool_calls\":[" +
               "{\"id\":\"c1\",\"type\":\"function\",\"function\":{\"name\":\"" + name + "\",\"arguments\":" + argsEncoded + "}}]}}]}";
    }

    [Fact]
    public async Task Act_executes_tool_then_returns_final_answer()
    {
        using var engine = new Engine();
        var tool = new RecordingTool("echo_tool");
        var provider = new SequencedProvider(new[]
        {
            // turn 1: ask to call echo_tool
            new LlmResponse { Content = "", RawResponseBody = ToolCallBody("echo_tool", "{\"text\":\"hi\"}") },
            // turn 2: final answer, no tool call
            new LlmResponse { Content = "Done: hi", RawResponseBody = null },
        });

        var facade = new JsLlmFacade(engine, provider, CancellationToken.None, new IBaseTool[] { tool });

        var result = await facade.act("please echo hi", null);

        Assert.Equal("Done: hi", result.Get("output").AsString());
        Assert.Equal(1, tool.CallCount);
        Assert.Equal("hi", tool.LastArgs?["text"]?.ToString());
        Assert.Equal(2, provider.ChatCalls);
        // The provider must have received the tool schema on the tool-calling turn.
        Assert.True(provider.LastConfigHadTools);
    }

    [Fact]
    public async Task Act_without_tools_returns_first_completion()
    {
        using var engine = new Engine();
        var provider = new SequencedProvider(new[]
        {
            new LlmResponse { Content = "just an answer", RawResponseBody = null },
        });

        var facade = new JsLlmFacade(engine, provider, CancellationToken.None, tools: null);

        var result = await facade.act("hello", null);

        Assert.Equal("just an answer", result.Get("output").AsString());
        Assert.False(provider.LastConfigHadTools);
    }

    // ── fakes ────────────────────────────────────────────────────────────────

    private sealed class SequencedProvider : ILlmProvider
    {
        private readonly LlmResponse[] _responses;
        private int _i;
        public int ChatCalls { get; private set; }
        public bool LastConfigHadTools { get; private set; }

        public SequencedProvider(LlmResponse[] responses) => _responses = responses;

        public string Name => "fake";
        public LlmConfig? BaseConfig => LlmConfig.Default() with { Model = "fake-model" };

        public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken ct = default)
            => Task.FromResult(_responses[Math.Min(_i++, _responses.Length - 1)]);

        public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken ct = default)
        {
            ChatCalls++;
            LastConfigHadTools = config?.Tools is { Count: > 0 };
            return Task.FromResult(_responses[Math.Min(_i++, _responses.Length - 1)]);
        }
    }

    private sealed class RecordingTool : IBaseTool
    {
        public RecordingTool(string name) => Name = name;
        public string Name { get; }
        public string Description => "test tool";
        public ToolSchema Schema => new(Name, Description, new Dictionary<string, ParameterSchema>());
        public int CallCount { get; private set; }
        public IReadOnlyDictionary<string, object?>? LastArgs { get; private set; }

        public Task<ToolCallResponse> CallAsync(ProtocolToolCallRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastArgs = request.Parameters;
            return Task.FromResult(new ToolCallResponse(true, new Dictionary<string, object?> { ["echoed"] = "hi" }, null));
        }

        public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
            => Task.FromResult(ToolResult.CreateSuccess("ok"));

        public bool ValidateInput(string input) => true;
    }
}
