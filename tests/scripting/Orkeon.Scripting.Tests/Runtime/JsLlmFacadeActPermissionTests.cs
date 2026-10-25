using Jint;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Autonomous;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Scripting.Runtime;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.Timing;
using ProtocolToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;

namespace Orkeon.Scripting.Tests.Runtime;

/// <summary>
/// The per-tool-call permission hook inside <c>ctx.llm.act</c>. A Deny verdict
/// must short-circuit the tool execution and feed a motivated <c>DENIED:</c> refusal back
/// to the model as the tool result (no exception); a null gate keeps the ungated path.
/// </summary>
public sealed class JsLlmFacadeActPermissionTests
{
    private static string ToolCallBody(string name, string argsJson)
    {
        var argsEncoded = System.Text.Json.JsonSerializer.Serialize(argsJson);
        return "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"\",\"tool_calls\":[" +
               "{\"id\":\"c1\",\"type\":\"function\",\"function\":{\"name\":\"" + name + "\",\"arguments\":" + argsEncoded + "}}]}}]}";
    }

    private static Jint.Native.JsValue EvalOptions(Engine engine, string js) => engine.Evaluate(js);

    [Fact]
    public async Task Deny_of_one_call_answers_it_in_the_tool_role_and_still_runs_the_allowed_one()
    {
        using var engine = new Engine(options => options.Constraints.PromiseTimeout = HangGuards.EnginePromise);
        var write = new RecordingTool("file_write");
        var read = new RecordingTool("file_read");
        var body = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"\",\"tool_calls\":[" +
                   "{\"id\":\"w1\",\"type\":\"function\",\"function\":{\"name\":\"file_write\",\"arguments\":\"{}\"}}," +
                   "{\"id\":\"r1\",\"type\":\"function\",\"function\":{\"name\":\"file_read\",\"arguments\":\"{}\"}}]}}]}";
        var provider = new MessageCapturingProvider(new[]
        {
            new LlmResponse { Content = "", RawResponseBody = body },
            new LlmResponse { Content = "read only", RawResponseBody = null },
        });
        var gate = new ScriptedGate((toolName, mode) => toolName == "file_write"
            ? PermissionVerdict.Deny($"tool '{toolName}' is not permitted in mode '{mode}'.")
            : PermissionVerdict.Allow());

        var facade = new JsLlmFacade(
            engine, provider, CancellationToken.None, new IBaseTool[] { write, read }, new JsLlmActGuards { PermissionGate = gate });

        var result = await facade.ActAsync(engine, "write then read", null);

        Assert.Equal("read only", result.Get("output").AsString());
        Assert.Equal(0, write.CallCount);
        Assert.Equal(1, read.CallCount);
        var tools = provider.MessagesPerCall[1].Where(m => m.Role == "tool").ToList();
        Assert.Equal("w1,r1", string.Join(",", tools.Select(m => m.ToolCallId)));
        Assert.StartsWith("DENIED:", tools[0].Content, StringComparison.Ordinal);
        Assert.DoesNotContain("DENIED:", tools[1].Content, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-01")]
    public async Task A_tool_the_act_does_not_offer_is_refused_without_asking_the_gate()
    {
        using var engine = new Engine(options => options.Constraints.PromiseTimeout = HangGuards.EnginePromise);
        var read = new RecordingTool("file_read");
        var provider = new MessageCapturingProvider(new[]
        {
            new LlmResponse { Content = "", RawResponseBody = ToolCallBody("read", "{\"path\":\"/x\"}") },
            new LlmResponse { Content = "gave up", RawResponseBody = null },
        });
        var gate = new ScriptedGate((_, _) => PermissionVerdict.Ask("approve?"));

        var facade = new JsLlmFacade(
            engine, provider, CancellationToken.None, new IBaseTool[] { read }, new JsLlmActGuards { PermissionGate = gate });

        var result = await facade.ActAsync(engine, "read it", EvalOptions(engine, "({ permissionMode: 'acceptEdits' })"));

        Assert.Equal("gave up", result.Get("output").AsString());
        Assert.Null(gate.LastMode);
        Assert.Equal(0, read.CallCount);
        var tool = Assert.Single(provider.MessagesPerCall[1], m => m.Role == "tool");
        Assert.Equal("ERROR: tool 'read' is not available to this agent.", tool.Content);
    }

    [Fact]
    public async Task The_engine_clock_stops_while_the_gate_decides_and_runs_again_after()
    {
        var clock = new PausableTimeConstraint(TimeSpan.FromMinutes(5));
        using var engine = new Engine(o => { o.Constraint(clock); o.Constraints.PromiseTimeout = HangGuards.EnginePromise; });
        var tool = new RecordingTool("file_write");
        var provider = new MessageCapturingProvider(new[]
        {
            new LlmResponse { Content = "", RawResponseBody = ToolCallBody("file_write", "{\"path\":\"/x\"}") },
            new LlmResponse { Content = "written", RawResponseBody = null },
        });
        bool? pausedWhileDeciding = null;
        var gate = new ScriptedGate((_, _) =>
        {
            pausedWhileDeciding = clock.IsPaused;
            return PermissionVerdict.Allow();
        });

        var facade = new JsLlmFacade(
            engine, provider, CancellationToken.None, new IBaseTool[] { tool }, new JsLlmActGuards { PermissionGate = gate });

        var result = await facade.ActAsync(engine, "write something", null);

        Assert.Equal("written", result.Get("output").AsString());
        Assert.True(pausedWhileDeciding);
        Assert.False(clock.IsPaused);
        Assert.Equal(1, tool.CallCount);
    }

    [Fact]
    public async Task An_approval_longer_than_the_wall_time_budget_does_not_end_the_turn()
    {
        using var engine = new Engine(options => options.Constraints.PromiseTimeout = HangGuards.EnginePromise);
        var clock = new ManualTimeProvider();
        var budget = new AgentExecutionBudget { MaxWallTime = TimeSpan.FromSeconds(60), TimeProvider = clock };
        var tool = new RecordingTool("file_write");
        var provider = new MessageCapturingProvider(new[]
        {
            new LlmResponse { Content = "", RawResponseBody = ToolCallBody("file_write", "{\"path\":\"/x\"}") },
            new LlmResponse { Content = "written", RawResponseBody = null },
        });
        // The operator takes 90 s to answer yes.
        var gate = new ScriptedGate((_, _) =>
        {
            clock.Advance(TimeSpan.FromSeconds(90));
            return PermissionVerdict.Allow();
        });

        var facade = new JsLlmFacade(
            engine, provider, CancellationToken.None, new IBaseTool[] { tool },
            new JsLlmActGuards { PermissionGate = gate, Budget = budget });

        var result = await facade.ActAsync(engine, "write something", null);

        Assert.Equal("written", result.Get("output").AsString());
        Assert.Equal(1, tool.CallCount);
        Assert.True(budget.Elapsed < TimeSpan.FromSeconds(60));
    }

    [Fact]
    public async Task The_wall_time_budget_still_ends_an_agent_that_itself_runs_past_it()
    {
        using var engine = new Engine(options => options.Constraints.PromiseTimeout = HangGuards.EnginePromise);
        var clock = new ManualTimeProvider();
        var budget = new AgentExecutionBudget { MaxWallTime = TimeSpan.FromSeconds(60), TimeProvider = clock };
        // The tool itself runs 90 s; the gate answers at once.
        var tool = new RecordingTool("file_write") { OnCall = () => clock.Advance(TimeSpan.FromSeconds(90)) };
        var provider = new MessageCapturingProvider(new[]
        {
            new LlmResponse { Content = "", RawResponseBody = ToolCallBody("file_write", "{\"path\":\"/x\"}") },
            new LlmResponse { Content = "never reached", RawResponseBody = null },
        });
        var gate = new ScriptedGate((_, _) => PermissionVerdict.Allow());

        var facade = new JsLlmFacade(
            engine, provider, CancellationToken.None, new IBaseTool[] { tool },
            new JsLlmActGuards { PermissionGate = gate, Budget = budget });

        var ex = await Assert.ThrowsAsync<BudgetExhaustedException>(() => facade.ActAsync(engine, "write something", null));

        Assert.Equal(BudgetDimension.WallTime, ex.Dimension);
        Assert.Single(provider.MessagesPerCall);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_script_reads_whether_the_host_registered_a_permission_gate(bool registered)
    {
        using var engine = new Engine(options => options.Constraints.PromiseTimeout = HangGuards.EnginePromise);
        var guards = registered ? new JsLlmActGuards { PermissionGate = new ScriptedGate((_, _) => PermissionVerdict.Allow()) } : null;
        var facade = new JsLlmFacade(engine, provider: null, CancellationToken.None, tools: null, guards);
        engine.SetValue("llm", facade);

        Assert.Equal(registered, engine.Evaluate("llm.permissionGate.enabled").AsBoolean());
    }

    [Fact]
    public async Task A_permissionMode_without_a_gate_logs_one_warning_and_the_tool_still_runs()
    {
        using var engine = new Engine(options => options.Constraints.PromiseTimeout = HangGuards.EnginePromise);
        var tool = new RecordingTool("file_write");
        var provider = new MessageCapturingProvider(new[]
        {
            new LlmResponse { Content = "", RawResponseBody = ToolCallBody("file_write", "{\"path\":\"/x\"}") },
            new LlmResponse { Content = "written", RawResponseBody = null },
        });
        var logger = new RecordingLogger();

        var facade = new JsLlmFacade(
            engine, provider, CancellationToken.None, new IBaseTool[] { tool }, guards: null,
            new JsLlmObservability { Logger = logger, AgentName = "coder" });

        var result = await facade.ActAsync(engine, "write something", EvalOptions(engine, "({ permissionMode: \"plan\" })"));

        Assert.Equal("written", result.Get("output").AsString());
        Assert.Equal(1, tool.CallCount);
        var warning = Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("'plan'", warning.Message, StringComparison.Ordinal);
        Assert.Contains("no permission gate", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_permissionMode_or_with_a_gate_no_warning_is_logged()
    {
        using var engine = new Engine(options => options.Constraints.PromiseTimeout = HangGuards.EnginePromise);
        var provider = new MessageCapturingProvider(new[] { new LlmResponse { Content = "done", RawResponseBody = null } });
        var logger = new RecordingLogger();
        var observability = new JsLlmObservability { Logger = logger };

        var ungated = new JsLlmFacade(engine, provider, CancellationToken.None, tools: null, guards: null, observability);
        await ungated.ActAsync(engine, "hello", null);
        var gated = new JsLlmFacade(
            engine, provider, CancellationToken.None, tools: null,
            new JsLlmActGuards { PermissionGate = new ScriptedGate((_, _) => PermissionVerdict.Allow()) }, observability);
        await gated.ActAsync(engine, "hello", EvalOptions(engine, "({ permissionMode: \"plan\" })"));

        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Deny_skips_the_tool_and_feeds_a_DENIED_result_back_to_the_model()
    {
        using var engine = new Engine(options => options.Constraints.PromiseTimeout = HangGuards.EnginePromise);
        var tool = new RecordingTool("file_write");
        var provider = new MessageCapturingProvider(new[]
        {
            new LlmResponse { Content = "", RawResponseBody = ToolCallBody("file_write", "{\"path\":\"/x\"}") },
            new LlmResponse { Content = "understood, stopping", RawResponseBody = null },
        });
        var gate = new ScriptedGate((toolName, mode) =>
            PermissionVerdict.Deny($"tool '{toolName}' is not permitted in mode '{mode}'."));

        var facade = new JsLlmFacade(
            engine, provider, CancellationToken.None, new IBaseTool[] { tool }, new JsLlmActGuards { PermissionGate = gate });

        var result = await facade.ActAsync(engine, "write something", null);

        Assert.Equal("understood, stopping", result.Get("output").AsString());
        Assert.Equal(0, tool.CallCount); // the tool was never executed
        Assert.Equal("default", gate.LastMode); // no permissionMode option → "default"
        // The refusal came back to the model as the tool result of turn 2.
        var turn2 = provider.MessagesPerCall[1];
        Assert.Contains(turn2, m => m.Role == "tool" && m.Content.Contains("DENIED:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Allow_executes_the_tool_and_passes_the_act_permissionMode_option()
    {
        using var engine = new Engine(options => options.Constraints.PromiseTimeout = HangGuards.EnginePromise);
        var tool = new RecordingTool("file_read");
        var provider = new MessageCapturingProvider(new[]
        {
            new LlmResponse { Content = "", RawResponseBody = ToolCallBody("file_read", "{\"path\":\"/x\"}") },
            new LlmResponse { Content = "done", RawResponseBody = null },
        });
        var gate = new ScriptedGate((_, _) => PermissionVerdict.Allow());

        var facade = new JsLlmFacade(
            engine, provider, CancellationToken.None, new IBaseTool[] { tool }, new JsLlmActGuards { PermissionGate = gate });

        var options = EvalOptions(engine, "({ permissionMode: \"acceptEdits\" })");
        var result = await facade.ActAsync(engine, "read something", options);

        Assert.Equal("done", result.Get("output").AsString());
        Assert.Equal(1, tool.CallCount);
        Assert.Equal("acceptEdits", gate.LastMode);
    }

    [Fact]
    public async Task Denied_calls_consume_no_tool_call_budget()
    {
        using var engine = new Engine(options => options.Constraints.PromiseTimeout = HangGuards.EnginePromise);
        var tool = new RecordingTool("file_write");
        var provider = new MessageCapturingProvider(new[]
        {
            new LlmResponse { Content = "", RawResponseBody = ToolCallBody("file_write", "{}") },
            new LlmResponse { Content = "ok", RawResponseBody = null },
        });
        var gate = new ScriptedGate((_, _) => PermissionVerdict.Deny("nope"));
        var budget = new Orkeon.Domain.Autonomous.AgentExecutionBudget { MaxToolCalls = 5 };

        var facade = new JsLlmFacade(
            engine, provider, CancellationToken.None, new IBaseTool[] { tool }, new JsLlmActGuards { Budget = budget, PermissionGate = gate });

        await facade.ActAsync(engine, "write", null);

        Assert.Equal(0, budget.CurrentToolCalls);
    }

    [Fact]
    public async Task Declared_tool_access_is_forwarded_to_the_gate()
    {
        using var engine = new Engine(options => options.Constraints.PromiseTimeout = HangGuards.EnginePromise);
        var tool = new RecordingTool("my_probe", ToolAccess.Read);
        var provider = new MessageCapturingProvider(new[]
        {
            new LlmResponse { Content = "", RawResponseBody = ToolCallBody("my_probe", "{}") },
            new LlmResponse { Content = "done", RawResponseBody = null },
        });
        var gate = new ScriptedGate((_, _) => PermissionVerdict.Allow());

        var facade = new JsLlmFacade(
            engine, provider, CancellationToken.None, new IBaseTool[] { tool }, new JsLlmActGuards { PermissionGate = gate });

        await facade.ActAsync(engine, "probe", null);

        Assert.Equal(ToolAccess.Read, gate.LastDeclaredAccess);
    }

    [Fact]
    [Trait("BR", "CH-01")]
    public async Task Unresolved_tool_never_reaches_the_gate()
    {
        using var engine = new Engine(options => options.Constraints.PromiseTimeout = HangGuards.EnginePromise);
        var provider = new MessageCapturingProvider(new[]
        {
            new LlmResponse { Content = "", RawResponseBody = ToolCallBody("ghost_tool", "{}") },
            new LlmResponse { Content = "done", RawResponseBody = null },
        });
        var gate = new ScriptedGate((_, _) => PermissionVerdict.Allow());

        var facade = new JsLlmFacade(
            engine, provider, CancellationToken.None, Array.Empty<IBaseTool>(), new JsLlmActGuards { PermissionGate = gate });

        var result = await facade.ActAsync(engine, "probe", null);

        Assert.Equal("done", result.Get("output").AsString());
        Assert.Null(gate.LastDeclaredAccess);
        // The tool does not exist: the loop feeds an ERROR back without consulting the gate (#193).
        var turn2 = provider.MessagesPerCall[1];
        Assert.Contains(turn2, m => m.Role == "tool" && m.Content.Contains("ERROR:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Null_gate_keeps_the_ungated_behaviour()
    {
        using var engine = new Engine(options => options.Constraints.PromiseTimeout = HangGuards.EnginePromise);
        var tool = new RecordingTool("file_write");
        var provider = new MessageCapturingProvider(new[]
        {
            new LlmResponse { Content = "", RawResponseBody = ToolCallBody("file_write", "{}") },
            new LlmResponse { Content = "done", RawResponseBody = null },
        });

        var facade = new JsLlmFacade(engine, provider, CancellationToken.None, new IBaseTool[] { tool });

        var result = await facade.ActAsync(engine, "write", null);

        Assert.Equal("done", result.Get("output").AsString());
        Assert.Equal(1, tool.CallCount);
    }

    // ── GAP-09: an allowed call goes through the host's invocation point ─────

    [Fact]
    public async Task An_allowed_call_goes_through_the_invocation_point_and_its_result_is_tagged()
    {
        using var engine = new Engine(options => options.Constraints.PromiseTimeout = HangGuards.EnginePromise);
        var tool = new RecordingTool("web_scrape");
        var provider = new MessageCapturingProvider(new[]
        {
            new LlmResponse { Content = "", RawResponseBody = ToolCallBody("web_scrape", "{\"url\":\"https://example.test\"}") },
            new LlmResponse { Content = "done", RawResponseBody = null },
        });
        var guardian = new ScriptedGuardian(_ => GuardResult.Allow());
        var pipeline = new Orkeon.Application.Services.Security.ToolInvocationPipeline(guardian, new TaggingSanitizer());

        var facade = new JsLlmFacade(
            engine, provider, CancellationToken.None, new IBaseTool[] { tool }, guards: null,
            new JsLlmObservability { ToolInvocation = pipeline, AgentName = "scraper" });

        await facade.ActAsync(engine, "read the page", null);

        Assert.Equal(1, tool.CallCount);
        var checkedCall = Assert.Single(guardian.Contexts);
        Assert.Equal(GuardPhase.ToolExecution, checkedCall.Phase);
        Assert.Equal("scraper", checkedCall.AgentRole);
        var turn2 = provider.MessagesPerCall[1];
        Assert.Contains(turn2, m => m.Content.Contains("[DATA]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_guardian_block_feeds_a_DENIED_result_back_and_the_tool_never_runs()
    {
        using var engine = new Engine(options => options.Constraints.PromiseTimeout = HangGuards.EnginePromise);
        var tool = new RecordingTool("file_read");
        var provider = new MessageCapturingProvider(new[]
        {
            new LlmResponse { Content = "", RawResponseBody = ToolCallBody("file_read", "{\"path\":\"../../etc/passwd\"}") },
            new LlmResponse { Content = "stopping", RawResponseBody = null },
        });
        var guardian = new ScriptedGuardian(_ => GuardResult.Block("Path traversal detected in tool argument 'path'", []));
        var pipeline = new Orkeon.Application.Services.Security.ToolInvocationPipeline(guardian);

        var facade = new JsLlmFacade(
            engine, provider, CancellationToken.None, new IBaseTool[] { tool }, guards: null,
            new JsLlmObservability { ToolInvocation = pipeline });

        await facade.ActAsync(engine, "read", null);

        Assert.Equal(0, tool.CallCount);
        var turn2 = provider.MessagesPerCall[1];
        Assert.Contains(turn2, m => m.Content.Contains("DENIED: Blocked by Guardian (ToolExecution): Path traversal", StringComparison.Ordinal));
    }

    private sealed class ScriptedGuardian : IGuardianPipeline
    {
        private readonly Func<GuardContext, GuardResult> _verdict;
        public ScriptedGuardian(Func<GuardContext, GuardResult> verdict) => _verdict = verdict;
        public List<GuardContext> Contexts { get; } = new();

        public Task<GuardResult> ExecuteAsync(GuardContext context, CancellationToken ct = default)
        {
            Contexts.Add(context);
            return Task.FromResult(_verdict(context));
        }
    }

    private sealed class TaggingSanitizer : IToolResultSanitizer
    {
        public ToolResultSanitization Sanitize(string toolName, string result, string agentRole)
            => new() { Text = $"[DATA]{result}[/DATA]" };
    }

    // ── fakes ────────────────────────────────────────────────────────────────

    private sealed class ScriptedGate : IPermissionGate
    {
        private readonly Func<string, string, PermissionVerdict> _decide;
        public string? LastMode { get; private set; }
        public ToolAccess? LastDeclaredAccess { get; private set; }

        public ScriptedGate(Func<string, string, PermissionVerdict> decide) => _decide = decide;

        public Task<PermissionVerdict> CheckAsync(
            string toolName, IReadOnlyDictionary<string, object?> arguments, string mode,
            ToolAccess declaredAccess = ToolAccess.Unspecified,
            CancellationToken cancellationToken = default)
        {
            LastMode = mode;
            LastDeclaredAccess = declaredAccess;
            return Task.FromResult(_decide(toolName, mode));
        }
    }

    private sealed class MessageCapturingProvider : ILlmProvider
    {
        private readonly LlmResponse[] _responses;
        private int _i;
        public List<LlmMessage[]> MessagesPerCall { get; } = new();

        public MessageCapturingProvider(LlmResponse[] responses) => _responses = responses;

        public string Name => "fake";
        public LlmConfig? BaseConfig => LlmConfig.Create("fake-model");

        public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken ct = default)
            => Task.FromResult(_responses[Math.Min(_i++, _responses.Length - 1)]);

        public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken ct = default)
        {
            MessagesPerCall.Add(messages);
            return Task.FromResult(_responses[Math.Min(_i++, _responses.Length - 1)]);
        }
    }

    private sealed class RecordingTool : IBaseTool
    {
        public RecordingTool(string name, ToolAccess access = ToolAccess.Unspecified)
        {
            Name = name;
            Access = access;
        }

        public string Name { get; }
        public ToolAccess Access { get; }
        public string Description => "test tool";
        public ToolSchema Schema => new(Name, Description, new Dictionary<string, ParameterSchema>());
        public int CallCount { get; private set; }
        public Action? OnCall { get; init; }

        public Task<ToolCallResponse> CallAsync(ProtocolToolCallRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            OnCall?.Invoke();
            return Task.FromResult(new ToolCallResponse(true, new Dictionary<string, object?> { ["ok"] = true }, null));
        }

        public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
            => Task.FromResult(ToolResult.CreateSuccess("ok"));

        public bool ValidateInput(string input) => true;
    }
}
