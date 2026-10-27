using Jint;
using Orkeon.Domain.Autonomous;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Scripting.Runtime;
using Microsoft.Extensions.Logging;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.Timing;
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
        using var engine = new Engine(options => options.Constraints.PromiseTimeout = HangGuards.EnginePromise);
        var tool = new RecordingTool("echo_tool");
        // Single looping response: the model would call the tool forever.
        var provider = new SequencedProvider(new[]
        {
            new LlmResponse { Content = "", RawResponseBody = ToolCallBody("echo_tool", "{\"text\":\"hi\"}") },
        });
        var budget = new AgentExecutionBudget { MaxToolCalls = 1 };

        var facade = new JsLlmFacade(engine, provider, CancellationToken.None, new IBaseTool[] { tool }, new JsLlmActGuards { Budget = budget });

        var ex = await Assert.ThrowsAsync<BudgetExhaustedException>(() => facade.ActAsync(engine, "loop forever", null));

        Assert.Equal(BudgetDimension.ToolCalls, ex.Dimension);
        // Exactly one paid tool call and one LLM turn: the second iteration's pre-flight
        // gate throws BEFORE paying for another ChatAsync.
        Assert.Equal(1, tool.CallCount);
        Assert.Equal(1, provider.ChatCalls);
    }

    [Fact]
    public async Task Act_returns_a_final_answer_whose_usage_passes_the_token_budget()
    {
        using var engine = new Engine(options => options.Constraints.PromiseTimeout = HangGuards.EnginePromise);
        var provider = new SequencedProvider(new[]
        {
            new LlmResponse { Content = "big answer", RawResponseBody = null, TokensUsed = 9_000 },
        });
        var budget = new AgentExecutionBudget { MaxTokensConsumed = 100 };

        var facade = new JsLlmFacade(engine, provider, CancellationToken.None, tools: null, new JsLlmActGuards { Budget = budget });

        var result = await facade.ActAsync(engine, "hello", null);

        // The answer is whole: the call that passed the limit needs no other, and nothing is lost.
        Assert.Equal("big answer", result.Get("output").AsString());
        Assert.Equal(1, provider.ChatCalls);
        Assert.Equal(9_000, budget.CurrentTokensConsumed);
    }

    // ── token exhaustion ends the act with a wrap-up answer (#218) ─────────────

    private static SequencedProvider ReadsThenWrapsUp(LlmResponse wrapUp) => new(new[]
    {
        new LlmResponse { Content = "", RawResponseBody = ToolCallBody("echo_tool", "{\"text\":\"a\"}"), TokensUsed = 60 },
        new LlmResponse { Content = "", RawResponseBody = ToolCallBody("echo_tool", "{\"text\":\"b\"}"), TokensUsed = 60 },
        wrapUp,
    });

    [Fact]
    [Trait("BR", "CH-218")]
    public async Task ShouldReturnTheWrapUpAnswerMarkedCutShort_WhenTheActReachesTheTokenLimit()
    {
        // Arrange
        using var engine = new Engine(options => options.Constraints.PromiseTimeout = HangGuards.EnginePromise);
        var tool = new RecordingTool("echo_tool");
        var provider = ReadsThenWrapsUp(new LlmResponse { Content = "Here is what the reads found.", RawResponseBody = null, TokensUsed = 30 });
        var budget = new AgentExecutionBudget { MaxToolCalls = 10, MaxTokensConsumed = 100 };
        var facade = new JsLlmFacade(engine, provider, CancellationToken.None, new IBaseTool[] { tool }, new JsLlmActGuards { Budget = budget });

        // Act
        var result = await facade.ActAsync(engine, "read and summarise", null);

        // Assert: one wrap-up call, its tokens recorded, no second wrap-up
        Assert.Equal("Here is what the reads found.", result.Get("output").AsString());
        Assert.True(result.Get("budgetExhausted").AsBoolean());
        Assert.Equal(3, provider.ChatCalls);
        Assert.Equal(2, tool.CallCount);
        Assert.Equal(150, budget.CurrentTokensConsumed);
    }

    [Fact]
    [Trait("BR", "CH-218")]
    public async Task ShouldOfferNoToolAndSayTheBudgetIsSpent_WhenTheActWrapsUp()
    {
        // Arrange
        using var engine = new Engine(options => options.Constraints.PromiseTimeout = HangGuards.EnginePromise);
        var tool = new RecordingTool("echo_tool");
        var provider = ReadsThenWrapsUp(new LlmResponse { Content = "Summary.", RawResponseBody = null, TokensUsed = 30 });
        var budget = new AgentExecutionBudget { MaxToolCalls = 10, MaxTokensConsumed = 100 };
        var facade = new JsLlmFacade(engine, provider, CancellationToken.None, new IBaseTool[] { tool }, new JsLlmActGuards { Budget = budget });

        // Act
        await facade.ActAsync(engine, "read and summarise", null);

        // Assert
        Assert.NotEmpty(provider.Configs[0]!.Tools ?? []);
        var wrapUp = provider.Configs[2]!;
        Assert.Empty(wrapUp.Tools ?? []);
        Assert.Equal(ToolCallMode.None, wrapUp.ToolMode);
        var note = provider.Messages[2][^1];
        Assert.Equal("user", note.Role);
        Assert.Contains("budget", note.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("answer now", note.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("BR", "CH-218")]
    public async Task ShouldThrowBudgetExhausted_WhenTheWrapUpCallFails()
    {
        // Arrange
        using var engine = new Engine(options => options.Constraints.PromiseTimeout = HangGuards.EnginePromise);
        var tool = new RecordingTool("echo_tool");
        var provider = ReadsThenWrapsUp(new LlmResponse { Content = "never read", RawResponseBody = null });
        provider.FailOnCall = 3;
        var budget = new AgentExecutionBudget { MaxToolCalls = 10, MaxTokensConsumed = 100 };
        var facade = new JsLlmFacade(engine, provider, CancellationToken.None, new IBaseTool[] { tool }, new JsLlmActGuards { Budget = budget });

        // Act
        var ex = await Assert.ThrowsAsync<BudgetExhaustedException>(() => facade.ActAsync(engine, "read and summarise", null));

        // Assert
        Assert.Equal(BudgetDimension.Tokens, ex.Dimension);
        Assert.Equal("Token budget exhausted: 120 tokens sent and received over 2 calls (limit 100)", ex.Message);
        Assert.Equal(3, provider.ChatCalls);
    }

    [Fact]
    [Trait("BR", "CH-218")]
    public async Task ShouldThrowBudgetExhausted_WhenTheWrapUpAnswersNothing()
    {
        // Arrange
        using var engine = new Engine(options => options.Constraints.PromiseTimeout = HangGuards.EnginePromise);
        var tool = new RecordingTool("echo_tool");
        var provider = ReadsThenWrapsUp(new LlmResponse { Content = "", RawResponseBody = null, TokensUsed = 5 });
        var budget = new AgentExecutionBudget { MaxToolCalls = 10, MaxTokensConsumed = 100 };
        var facade = new JsLlmFacade(engine, provider, CancellationToken.None, new IBaseTool[] { tool }, new JsLlmActGuards { Budget = budget });

        // Act
        var ex = await Assert.ThrowsAsync<BudgetExhaustedException>(() => facade.ActAsync(engine, "read and summarise", null));

        // Assert
        Assert.Equal(BudgetDimension.Tokens, ex.Dimension);
        Assert.Equal(3, provider.ChatCalls);
        Assert.Equal(125, budget.CurrentTokensConsumed);
    }

    [Fact]
    [Trait("BR", "CH-218")]
    public async Task ShouldLogTheRunningSumNextToTheLimit_OnEachCall()
    {
        // Arrange
        using var engine = new Engine(options => options.Constraints.PromiseTimeout = HangGuards.EnginePromise);
        var tool = new RecordingTool("echo_tool");
        var provider = ReadsThenWrapsUp(new LlmResponse { Content = "Summary.", RawResponseBody = null, TokensUsed = 30 });
        var budget = new AgentExecutionBudget { MaxToolCalls = 10, MaxTokensConsumed = 100 };
        var logger = new RecordingLogger();
        var facade = new JsLlmFacade(
            engine, provider, CancellationToken.None, new IBaseTool[] { tool }, new JsLlmActGuards { Budget = budget },
            new JsLlmObservability { Logger = logger, AgentName = "reader" });

        // Act
        await facade.ActAsync(engine, "read and summarise", null);

        // Assert
        Assert.Contains(logger.Entries, e => e.Message.Contains("running sum 60/100 tokens over 1 call", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, e => e.Message.Contains("running sum 120/100 tokens over 2 calls", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning
            && e.Message.Contains("Token budget exhausted: 120 tokens sent and received over 2 calls (limit 100)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Act_with_exceeded_wall_time_throws_before_any_llm_call()
    {
        using var engine = new Engine(options => options.Constraints.PromiseTimeout = HangGuards.EnginePromise);
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

        var facade = new JsLlmFacade(engine, provider, CancellationToken.None, tools: null, new JsLlmActGuards { Budget = budget });

        var ex = await Assert.ThrowsAsync<BudgetExhaustedException>(() => facade.ActAsync(engine, "hello", null));

        Assert.Equal(BudgetDimension.WallTime, ex.Dimension);
        Assert.Equal(0, provider.ChatCalls);
    }

    [Fact]
    public async Task Act_within_budget_completes_and_accounts_usage()
    {
        using var engine = new Engine(options => options.Constraints.PromiseTimeout = HangGuards.EnginePromise);
        var tool = new RecordingTool("echo_tool");
        var provider = new SequencedProvider(new[]
        {
            new LlmResponse { Content = "", RawResponseBody = ToolCallBody("echo_tool", "{\"text\":\"hi\"}"), TokensUsed = 50 },
            new LlmResponse { Content = "Done", RawResponseBody = null, TokensUsed = 30 },
        });
        var budget = new AgentExecutionBudget { MaxToolCalls = 5, MaxTokensConsumed = 1_000 };

        var facade = new JsLlmFacade(engine, provider, CancellationToken.None, new IBaseTool[] { tool }, new JsLlmActGuards { Budget = budget });

        var result = await facade.ActAsync(engine, "please echo hi", null);

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
        public int FailOnCall { get; set; }
        public List<LlmConfig?> Configs { get; } = [];
        public List<LlmMessage[]> Messages { get; } = [];

        public SequencedProvider(LlmResponse[] responses) => _responses = responses;

        public string Name => "fake";
        public LlmConfig? BaseConfig => LlmConfig.Create("fake-model");

        public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken ct = default)
            => Task.FromResult(_responses[Math.Min(_i++, _responses.Length - 1)]);

        public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken ct = default)
        {
            ChatCalls++;
            Configs.Add(config);
            Messages.Add(messages);
            if (ChatCalls == FailOnCall)
                throw new HttpRequestException("provider unavailable");
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
