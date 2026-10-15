using Jint;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Constants.Agent;
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

        var result = await facade.ActAsync(engine, "please echo hi", null);

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

        var result = await facade.ActAsync(engine, "hello", null);

        Assert.Equal("just an answer", result.Get("output").AsString());
        Assert.False(provider.LastConfigHadTools);
    }

    private const string AnthropicTwoCallsBody =
        "{\"id\":\"msg_1\",\"type\":\"message\",\"role\":\"assistant\",\"content\":[" +
        "{\"type\":\"text\",\"text\":\"Reading both.\"}," +
        "{\"type\":\"tool_use\",\"id\":\"tu_1\",\"name\":\"echo_tool\",\"input\":{\"text\":\"a\"}}," +
        "{\"type\":\"tool_use\",\"id\":\"tu_2\",\"name\":\"other_tool\",\"input\":{\"text\":\"b\"}}]," +
        "\"stop_reason\":\"tool_use\"}";

    private static string OpenAiCallsBody(params (string Id, string Name)[] calls)
    {
        var items = calls.Select(c =>
            "{\"id\":\"" + c.Id + "\",\"type\":\"function\",\"function\":{\"name\":\"" + c.Name + "\",\"arguments\":\"{}\"}}");
        return "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"\",\"tool_calls\":[" +
               string.Join(",", items) + "]}}]}";
    }

    [Fact]
    public async Task Act_runs_every_tool_use_block_of_an_Anthropic_turn_and_replays_them_in_the_tool_role()
    {
        using var engine = new Engine();
        var echo = new RecordingTool("echo_tool");
        var other = new RecordingTool("other_tool");
        var provider = new SequencedProvider(new[]
        {
            new LlmResponse { Content = "Reading both.", RawResponseBody = AnthropicTwoCallsBody },
            new LlmResponse { Content = "Both read.", RawResponseBody = null },
        });
        var facade = new JsLlmFacade(engine, provider, CancellationToken.None, new IBaseTool[] { echo, other });

        var result = await facade.ActAsync(engine, "read both", null);

        Assert.Equal("Both read.", result.Get("output").AsString());
        Assert.Equal(1, echo.CallCount);
        Assert.Equal("a", echo.LastArgs?["text"]?.ToString());
        Assert.Equal(1, other.CallCount);
        Assert.Equal("b", other.LastArgs?["text"]?.ToString());
        var turn2 = provider.Requests[1];
        var assistant = Assert.Single(turn2, m => m.Role == "assistant");
        Assert.Equal("Reading both.", assistant.Content);
        Assert.Contains("\"tu_1\"", assistant.RawToolCalls, StringComparison.Ordinal);
        Assert.Contains("\"tu_2\"", assistant.RawToolCalls, StringComparison.Ordinal);
        Assert.Equal("tu_1,tu_2", string.Join(",", turn2.Where(m => m.Role == "tool").Select(m => m.ToolCallId)));
        Assert.DoesNotContain(turn2, m => m.Role == "user" && m.Content.Contains("[tool:", StringComparison.Ordinal));
        // The tool turns follow the assistant turn that asked for them.
        Assert.True(Array.IndexOf(turn2, assistant) < Array.FindIndex(turn2, m => m.Role == "tool"));
    }

    [Fact]
    public async Task Act_runs_every_tool_call_of_an_OpenAI_turn_and_answers_each_by_its_id()
    {
        using var engine = new Engine();
        var echo = new RecordingTool("echo_tool");
        var other = new RecordingTool("other_tool");
        var provider = new SequencedProvider(new[]
        {
            new LlmResponse { Content = "", RawResponseBody = OpenAiCallsBody(("c1", "echo_tool"), ("c2", "other_tool")) },
            new LlmResponse { Content = "Both done.", RawResponseBody = null },
        });
        var facade = new JsLlmFacade(engine, provider, CancellationToken.None, new IBaseTool[] { echo, other });

        var result = await facade.ActAsync(engine, "do both", null);

        Assert.Equal("Both done.", result.Get("output").AsString());
        Assert.Equal(1, echo.CallCount);
        Assert.Equal(1, other.CallCount);
        var turn2 = provider.Requests[1];
        var assistant = Assert.Single(turn2, m => m.Role == "assistant");
        Assert.Contains("\"c1\"", assistant.RawToolCalls, StringComparison.Ordinal);
        Assert.Contains("\"c2\"", assistant.RawToolCalls, StringComparison.Ordinal);
        Assert.Equal("c1,c2", string.Join(",", turn2.Where(m => m.Role == "tool").Select(m => m.ToolCallId)));
    }

    [Fact]
    public async Task Act_hands_a_file_read_result_whole_and_cuts_another_tool_once_at_the_framework_limit()
    {
        using var engine = new Engine();
        var big = new string('x', 20_000);
        var fileRead = new TextTool("file_read", big);
        var other = new TextTool("big_tool", big);
        var provider = new SequencedProvider(new[]
        {
            new LlmResponse { Content = "", RawResponseBody = OpenAiCallsBody(("c1", "file_read"), ("c2", "big_tool")) },
            new LlmResponse { Content = "read", RawResponseBody = null },
        });
        var facade = new JsLlmFacade(engine, provider, CancellationToken.None, new IBaseTool[] { fileRead, other });

        await facade.ActAsync(engine, "read", null);

        var results = provider.Requests[1].Where(m => m.Role == "tool").ToDictionary(m => m.ToolCallId!, m => m.Content);
        Assert.Contains(big, results["c1"], StringComparison.Ordinal);
        Assert.DoesNotContain(big, results["c2"], StringComparison.Ordinal);
        // Cut once, at the framework's bound, with the framework's notice: the 20 002 characters
        // of the serialized string less the bound.
        var bound = AgentDefaults.ResolveMaxToolResultLength("big_tool");
        Assert.StartsWith("\"" + new string('x', bound - 1) + "\n\n[... truncated, ", results["c2"], StringComparison.Ordinal);
        Assert.Contains($"{20_002 - bound} chars omitted", results["c2"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Act_does_not_cut_again_what_the_invocation_pipeline_returned()
    {
        using var engine = new Engine();
        var text = new string('y', 20_000);
        var pipeline = new StubToolInvocationPipeline(text);
        var provider = new SequencedProvider(new[]
        {
            new LlmResponse { Content = "", RawResponseBody = OpenAiCallsBody(("c1", "big_tool")) },
            new LlmResponse { Content = "read", RawResponseBody = null },
        });
        var facade = new JsLlmFacade(
            engine, provider, CancellationToken.None, new IBaseTool[] { new TextTool("big_tool", "unused") },
            observability: new JsLlmObservability { ToolInvocation = pipeline });

        await facade.ActAsync(engine, "read", null);

        Assert.Equal(1, pipeline.Calls);
        var tool = Assert.Single(provider.Requests[1], m => m.Role == "tool");
        Assert.Equal(text, tool.Content);
    }

    [Fact]
    public async Task Act_returns_the_provider_error_when_a_failed_turn_has_no_answer()
    {
        using var engine = new Engine();
        var provider = new SequencedProvider(new[]
        {
            new LlmResponse
            {
                Content = "",
                RawResponseBody = null,
                Metadata = new Dictionary<string, object> { ["error"] = "HTTP 401: invalid key" },
            },
        });
        var facade = new JsLlmFacade(engine, provider, CancellationToken.None, new IBaseTool[] { new RecordingTool("echo_tool") });

        var result = await facade.ActAsync(engine, "hello", null);

        Assert.Equal("<undefined-llm:HTTP 401: invalid key>", result.Get("output").AsString());
    }

    [Fact]
    public async Task Act_treats_a_tool_call_it_cannot_read_as_no_call()
    {
        using var engine = new Engine();
        var tool = new RecordingTool("echo_tool");
        var provider = new SequencedProvider(new[]
        {
            // A tool call without its function: unreadable, so the turn is the final answer.
            new LlmResponse
            {
                Content = "plain answer",
                RawResponseBody = "{\"choices\":[{\"message\":{\"content\":\"plain answer\",\"tool_calls\":[{\"id\":\"c1\"}]}}]}",
            },
        });
        var facade = new JsLlmFacade(engine, provider, CancellationToken.None, new IBaseTool[] { tool });

        var result = await facade.ActAsync(engine, "hello", null);

        Assert.Equal("plain answer", result.Get("output").AsString());
        Assert.Equal(0, tool.CallCount);
    }

    // ── fakes ────────────────────────────────────────────────────────────────

    private sealed class SequencedProvider : ILlmProvider
    {
        private readonly LlmResponse[] _responses;
        private int _i;
        public int ChatCalls { get; private set; }
        public bool LastConfigHadTools { get; private set; }

        /// <summary>The messages of each chat call, in order.</summary>
        public List<LlmMessage[]> Requests { get; } = new();

        public SequencedProvider(LlmResponse[] responses) => _responses = responses;

        public string Name => "fake";
        public LlmConfig? BaseConfig => LlmConfig.Create("fake-model");

        public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken ct = default)
            => Task.FromResult(_responses[Math.Min(_i++, _responses.Length - 1)]);

        public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken ct = default)
        {
            ChatCalls++;
            Requests.Add(messages.ToArray());
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

    /// <summary>A tool whose result is a fixed text.</summary>
    private sealed class TextTool : IBaseTool
    {
        private readonly string _text;
        public TextTool(string name, string text) { Name = name; _text = text; }
        public string Name { get; }
        public string Description => "text tool";
        public ToolSchema Schema => new(Name, Description, new Dictionary<string, ParameterSchema>());

        public Task<ToolCallResponse> CallAsync(ProtocolToolCallRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new ToolCallResponse(true, _text, null));

        public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
            => Task.FromResult(ToolResult.CreateSuccess(_text));

        public bool ValidateInput(string input) => true;
    }

    /// <summary>The host's invocation point, answering every call with the same conversation text.</summary>
    private sealed class StubToolInvocationPipeline : IToolInvocationPipeline
    {
        private readonly string _conversationText;
        public StubToolInvocationPipeline(string conversationText) => _conversationText = conversationText;
        public int Calls { get; private set; }

        public Task<ToolInvocationResult> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new ToolInvocationResult
            {
                ConversationText = _conversationText,
                RawText = _conversationText,
                Success = true,
            });
        }
    }
}
