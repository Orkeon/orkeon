using Jint;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Application.Services.Security;
using Orkeon.Domain.Autonomous;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Infrastructure.Security;
using Orkeon.Scripting.Runtime;
using Orkeon.Scripting.Tests.Doubles;
using Orkeon.Tests.Shared.Timing;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;

namespace Orkeon.Scripting.Tests.Runtime;

/// <summary>FWK-06: a tool call of <c>act</c> is put to the permission gate once, by the invocation pipeline.</summary>
public sealed class JsLlmFacadeSingleGateTests
{
    private static Jint.Native.JsValue AcceptEditsOptions(Engine engine) =>
        engine.Evaluate("({ permissionMode: 'acceptEdits' })");

    private sealed class CountingGate : IPermissionGate
    {
        public List<string> Modes { get; } = [];
        public PermissionVerdict Verdict { get; init; } = PermissionVerdict.Allow();
        public Action? OnCheck { get; init; }

        public Task<PermissionVerdict> CheckAsync(
            string toolName, IReadOnlyDictionary<string, object?> arguments, string mode,
            ToolAccess declaredAccess = ToolAccess.Unspecified, CancellationToken cancellationToken = default)
        {
            Modes.Add(mode);
            OnCheck?.Invoke();
            return Task.FromResult(Verdict);
        }
    }

    private sealed class CountingTool : IBaseTool
    {
        public string Name => "file_write";
        public string Description => "test tool";
        public ToolSchema Schema => new(Name, Description, new Dictionary<string, ParameterSchema>());
        public int CallCount { get; private set; }

        public Task<ToolCallResponse> CallAsync(ToolCallRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new ToolCallResponse(true, "done", null));
        }

        public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
            => Task.FromResult(ToolResult.CreateSuccess("ok"));

        public bool ValidateInput(string input) => true;
    }

    private sealed class ScriptedProvider : ILlmProvider
    {
        private int _i;
        private readonly LlmResponse[] _responses =
        [
            new LlmResponse
            {
                Content = "",
                RawResponseBody = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"\",\"tool_calls\":[{\"id\":\"c1\",\"type\":\"function\",\"function\":{\"name\":\"file_write\",\"arguments\":\"{}\"}}]}}]}",
            },
            new LlmResponse { Content = "finished", RawResponseBody = null },
        ];

        public string Name => "fake";
        public LlmConfig? BaseConfig => LlmConfig.Create("fake-model");

        public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken ct = default)
            => Task.FromResult(_responses[Math.Min(_i++, _responses.Length - 1)]);

        public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken ct = default)
            => Task.FromResult(_responses[Math.Min(_i++, _responses.Length - 1)]);
    }

    [Fact]
    public async Task A_call_is_put_to_the_gate_once_when_the_host_has_a_pipeline_with_that_gate()
    {
        using var engine = new Engine(options => options.Constraints.PromiseTimeout = HangGuards.EnginePromise);
        var gate = new CountingGate();
        var tool = new CountingTool();
        var pipeline = new ToolInvocationPipeline(permissionGate: gate);
        var facade = new JsLlmFacade(
            engine, new ScriptedProvider(), CancellationToken.None, new IBaseTool[] { tool },
            new JsLlmActGuards { PermissionGate = gate },
            new JsLlmObservability { ToolInvocation = pipeline, AgentName = "coder" });

        await facade.ActAsync(engine, "write", AcceptEditsOptions(engine));

        Assert.Equal(1, tool.CallCount);
        Assert.Equal("acceptEdits", Assert.Single(gate.Modes));
    }

    [Fact]
    public async Task A_denial_by_the_pipeline_gate_stops_the_call_and_the_gate_is_asked_once()
    {
        using var engine = new Engine(options => options.Constraints.PromiseTimeout = HangGuards.EnginePromise);
        var gate = new CountingGate { Verdict = PermissionVerdict.Deny("no writes") };
        var tool = new CountingTool();
        var pipeline = new ToolInvocationPipeline(permissionGate: gate);
        var facade = new JsLlmFacade(
            engine, new ScriptedProvider(), CancellationToken.None, new IBaseTool[] { tool },
            new JsLlmActGuards { PermissionGate = gate },
            new JsLlmObservability { ToolInvocation = pipeline, AgentName = "coder" });

        await facade.ActAsync(engine, "write", null);

        Assert.Equal(0, tool.CallCount);
        Assert.Single(gate.Modes);
    }

    [Fact]
    public async Task The_engine_clock_stops_while_the_pipeline_gate_decides()
    {
        var clock = new PausableTimeConstraint(TimeSpan.FromMinutes(5));
        using var engine = new Engine(o => { o.Constraint(clock); o.Constraints.PromiseTimeout = HangGuards.EnginePromise; });
        bool? pausedWhileDeciding = null;
        var gate = new CountingGate { OnCheck = () => pausedWhileDeciding = clock.IsPaused };
        var tool = new CountingTool();
        var facade = new JsLlmFacade(
            engine, new ScriptedProvider(), CancellationToken.None, new IBaseTool[] { tool },
            new JsLlmActGuards { PermissionGate = gate },
            new JsLlmObservability { ToolInvocation = new ToolInvocationPipeline(permissionGate: gate), AgentName = "coder" });

        await facade.ActAsync(engine, "write", null);

        Assert.True(pausedWhileDeciding);
        Assert.False(clock.IsPaused);
        Assert.Equal(1, tool.CallCount);
    }

    [Fact]
    public async Task An_approval_longer_than_the_wall_time_budget_does_not_end_the_turn_through_the_pipeline()
    {
        using var engine = new Engine(options => options.Constraints.PromiseTimeout = HangGuards.EnginePromise);
        var time = new ManualTimeProvider();
        var budget = new AgentExecutionBudget { MaxWallTime = TimeSpan.FromSeconds(60), TimeProvider = time };
        // The operator takes 90 s to answer yes.
        var gate = new CountingGate { OnCheck = () => time.Advance(TimeSpan.FromSeconds(90)) };
        var tool = new CountingTool();
        var facade = new JsLlmFacade(
            engine, new ScriptedProvider(), CancellationToken.None, new IBaseTool[] { tool },
            new JsLlmActGuards { PermissionGate = gate, Budget = budget },
            new JsLlmObservability { ToolInvocation = new ToolInvocationPipeline(permissionGate: gate), AgentName = "coder" });

        var result = await facade.ActAsync(engine, "write", null);

        Assert.Equal("finished", result.Get("output").AsString());
        Assert.Equal(1, tool.CallCount);
    }

    [Fact]
    public async Task An_onToolCall_ask_is_put_to_the_operator_once_when_the_pipeline_gates()
    {
        using var engine = new Engine(options => options.Constraints.PromiseTimeout = HangGuards.EnginePromise);
        var log = new List<string>();
        var channel = new StubToolApprovalChannel { Decision = ToolApprovalDecision.AllowOnce };
        var gate = new StubSequencePermissionGate(new ModePermissionGate(channel), log, null);
        var tool = new FakeRecordingTool("file_read", log);
        var facade = new JsLlmFacade(
            engine, new FakeToolCallingLlmProvider("file_read", "{\"path\":\"/x\"}"), CancellationToken.None, new IBaseTool[] { tool },
            new JsLlmActGuards { PermissionGate = gate },
            new JsLlmObservability { ToolInvocation = new ToolInvocationPipeline(permissionGate: gate), AgentName = "coder" });
        var options = await engine.EvaluateAsync(
            "({ permissionMode: 'default', onToolCall: (call) => 'ask' })", null, TestContext.Current.CancellationToken);

        await facade.ActAsync(engine, "read it", options);

        Assert.Single(gate.Paths);
        Assert.Single(channel.Asks);
        Assert.Equal(["/x"], tool.Inputs);
    }

    // SEC-2: through the pipeline, an onToolCall "ask" never relaxes plan: the write is refused before any operator is asked.
    [Fact]
    public async Task An_onToolCall_ask_under_plan_is_refused_without_asking_the_operator_when_the_pipeline_gates()
    {
        using var engine = new Engine(options => options.Constraints.PromiseTimeout = HangGuards.EnginePromise);
        var channel = new StubToolApprovalChannel { Decision = ToolApprovalDecision.AllowOnce };
        var gate = new ModePermissionGate(channel);
        var tool = new CountingTool();
        var facade = new JsLlmFacade(
            engine, new ScriptedProvider(), CancellationToken.None, new IBaseTool[] { tool },
            new JsLlmActGuards { PermissionGate = gate },
            new JsLlmObservability { ToolInvocation = new ToolInvocationPipeline(permissionGate: gate, options: new ToolInvocationPipelineOptions { ApprovalChannel = channel }), AgentName = "coder" });
        var options = await engine.EvaluateAsync(
            "({ permissionMode: 'plan', onToolCall: (call) => 'ask' })", null, TestContext.Current.CancellationToken);

        await facade.ActAsync(engine, "write", options);

        Assert.Equal(0, tool.CallCount);
        Assert.Empty(channel.Asks);
    }

    // SEC-5: through the pipeline, a call its gate refuses costs no tool-call budget; an admitted one costs one.
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public async Task Only_a_call_the_pipeline_gate_admits_is_charged_to_the_tool_call_budget(bool allowed, int charged)
    {
        using var engine = new Engine(options => options.Constraints.PromiseTimeout = HangGuards.EnginePromise);
        var budget = new AgentExecutionBudget { MaxToolCalls = 5 };
        var gate = new CountingGate { Verdict = allowed ? PermissionVerdict.Allow() : PermissionVerdict.Deny("no writes") };
        var tool = new CountingTool();
        var facade = new JsLlmFacade(
            engine, new ScriptedProvider(), CancellationToken.None, new IBaseTool[] { tool },
            new JsLlmActGuards { PermissionGate = gate, Budget = budget },
            new JsLlmObservability { ToolInvocation = new ToolInvocationPipeline(permissionGate: gate), AgentName = "coder" });

        await facade.ActAsync(engine, "write", null);

        Assert.Equal(charged, budget.CurrentToolCalls);
        Assert.Equal(charged, tool.CallCount);
    }

    [Fact]
    public async Task A_spent_tool_call_budget_ends_the_act_through_the_pipeline_the_tool_unrun()
    {
        using var engine = new Engine(options => options.Constraints.PromiseTimeout = HangGuards.EnginePromise);
        var budget = new AgentExecutionBudget { MaxToolCalls = 0 };
        var gate = new CountingGate();
        var tool = new CountingTool();
        var facade = new JsLlmFacade(
            engine, new ScriptedProvider(), CancellationToken.None, new IBaseTool[] { tool },
            new JsLlmActGuards { PermissionGate = gate, Budget = budget },
            new JsLlmObservability { ToolInvocation = new ToolInvocationPipeline(permissionGate: gate), AgentName = "coder" });

        await Assert.ThrowsAnyAsync<Exception>(() => facade.ActAsync(engine, "write", null));

        Assert.Equal(0, tool.CallCount);
    }

    [Fact]
    public async Task A_pipeline_that_does_not_gate_leaves_the_facade_gate_in_charge()
    {
        using var engine = new Engine(options => options.Constraints.PromiseTimeout = HangGuards.EnginePromise);
        var gate = new CountingGate { Verdict = PermissionVerdict.Deny("no writes") };
        var tool = new CountingTool();
        var pipeline = new ToolInvocationPipeline();
        var facade = new JsLlmFacade(
            engine, new ScriptedProvider(), CancellationToken.None, new IBaseTool[] { tool },
            new JsLlmActGuards { PermissionGate = gate },
            new JsLlmObservability { ToolInvocation = pipeline, AgentName = "coder" });

        await facade.ActAsync(engine, "write", null);

        Assert.False(pipeline.GatesPermissions);
        Assert.Equal(0, tool.CallCount);
        Assert.Single(gate.Modes);
    }
}
