using Jint;
using Orkeon.Domain.Autonomous;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Scripting.Runtime;
using ProtocolToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;

namespace Orkeon.Scripting.Tests.Runtime;

/// <summary>
/// <see cref="AgentExecutionBudget"/> enforcement inside the
/// <c>ctx.llm.act</c> tool-calling loop — pre-iteration gate, per-tool-call and per-token
/// accounting. A null budget must leave the loop byte-identical to the pre-F1 behaviour
/// (covered by the untouched <see cref="JsLlmFacadeActTests"/>).
/// </summary>
public sealed class JsLlmFacadeBudgetTests
{
    private static string ToolCallBody(string name, string argsJson)
    {
        var argsEncoded = System.Text.Json.JsonSerializer.Serialize(argsJson);
        return "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"\",\"tool_calls\":[" +
               "{\"id\":\"c1\",\"type\":\"function\",\"function\":{\"name\":\"" + name + "\",\"arguments\":" + argsEncoded + "}}]}}]}";
    }

    [Fact]
    public async Task Act_with_toolCalls_budget_1_throws_at_second_iteration_gate()
    {
        using var engine = new Engine();
        var tool = new RecordingTool("echo_tool");
        // Single looping response: the model would call the tool forever.
        var provider = new SequencedProvider(new[]
        {
            new LlmResponse { Content = "", RawResponseBody = ToolCallBody("echo_tool", "{\"text\":\"hi\"}") },
        });
        var budget = new AgentExecutionBudget { MaxToolCalls = 1 };

        var facade = new JsLlmFacade(engine, provider, CancellationToken.None, new IBaseTool[] { tool }, budget);

        var ex = await Assert.ThrowsAsync<BudgetExhaustedException>(() => facade.act("loop forever", null));

        Assert.Equal(BudgetDimension.ToolCalls, ex.Dimension);
        // Exactly one paid tool call and one LLM turn: the second iteration's pre-flight
        // gate throws BEFORE paying for another ChatAsync.
        Assert.Equal(1, tool.CallCount);
        Assert.Equal(1, provider.ChatCalls);
    }

    [Fact]
    public async Task Act_with_token_budget_throws_when_response_usage_exceeds_it()
    {
        using var engine = new Engine();
        var provider = new SequencedProvider(new[]
        {
            new LlmResponse { Content = "big answer", RawResponseBody = null, TokensUsed = 9_000 },
        });
        var budget = new AgentExecutionBudget { MaxTokensConsumed = 100 };

        var facade = new JsLlmFacade(engine, provider, CancellationToken.None, tools: null, budget);

        var ex = await Assert.ThrowsAsync<BudgetExhaustedException>(() => facade.act("hello", null));

        Assert.Equal(BudgetDimension.Tokens, ex.Dimension);
    }

    [Fact]
    public async Task Act_with_exceeded_wall_time_throws_before_any_llm_call()
    {
        using var engine = new Engine();
        var provider = new SequencedProvider(new[]
        {
            new LlmResponse { Content = "never reached", RawResponseBody = null },
        });
        var clock = new ManualTimeProvider();
        var budget = new AgentExecutionBudget
        {
            MaxWallTime = TimeSpan.FromMinutes(5),
            TimeProvider = clock,
        };
        clock.Advance(TimeSpan.FromMinutes(6));

        var facade = new JsLlmFacade(engine, provider, CancellationToken.None, tools: null, budget);

        var ex = await Assert.ThrowsAsync<BudgetExhaustedException>(() => facade.act("hello", null));

        Assert.Equal(BudgetDimension.WallTime, ex.Dimension);
        Assert.Equal(0, provider.ChatCalls);
    }

    [Fact]
    public async Task Act_within_budget_completes_and_accounts_usage()
    {
        using var engine = new Engine();
        var tool = new RecordingTool("echo_tool");
        var provider = new SequencedProvider(new[]
        {
            new LlmResponse { Content = "", RawResponseBody = ToolCallBody("echo_tool", "{\"text\":\"hi\"}"), TokensUsed = 50 },
            new LlmResponse { Content = "Done", RawResponseBody = null, TokensUsed = 30 },
        });
        var budget = new AgentExecutionBudget { MaxToolCalls = 5, MaxTokensConsumed = 1_000 };

        var facade = new JsLlmFacade(engine, provider, CancellationToken.None, new IBaseTool[] { tool }, budget);

        var result = await facade.act("please echo hi", null);

        Assert.Equal("Done", result.Get("output").AsString());
        Assert.Equal(1, budget.CurrentToolCalls);
        Assert.Equal(80, budget.CurrentTokensConsumed);
    }

    // ── fakes (same shapes as JsLlmFacadeActTests) ───────────────────────────

    private sealed class SequencedProvider : ILlmProvider
    {
        private readonly LlmResponse[] _responses;
        private int _i;
        public int ChatCalls { get; private set; }

        public SequencedProvider(LlmResponse[] responses) => _responses = responses;

        public string Name => "fake";
        public LlmConfig? BaseConfig => LlmConfig.Default() with { Model = "fake-model" };

        public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken ct = default)
            => Task.FromResult(_responses[Math.Min(_i++, _responses.Length - 1)]);

        public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken ct = default)
        {
            ChatCalls++;
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

        public Task<ToolCallResponse> CallAsync(ProtocolToolCallRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new ToolCallResponse(true, new Dictionary<string, object?> { ["echoed"] = "hi" }, null));
        }

        public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
            => Task.FromResult(ToolResult.CreateSuccess("ok"));

        public bool ValidateInput(string input) => true;
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;
        public override long GetTimestamp() => _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Advance(TimeSpan delta) => _timestamp += (long)(delta.TotalSeconds * TimestampFrequency);
    }
}
