using Jint;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Scripting.Runtime;
using ProtocolToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;

namespace Orkeon.Scripting.Tests.Runtime;

/// <summary>
/// F2 (exp07): the per-tool-call permission hook inside <c>ctx.llm.act</c>. A Deny verdict
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
    public async Task Deny_skips_the_tool_and_feeds_a_DENIED_result_back_to_the_model()
    {
        using var engine = new Engine();
        var tool = new RecordingTool("file_write");
        var provider = new MessageCapturingProvider(new[]
        {
            new LlmResponse { Content = "", RawResponseBody = ToolCallBody("file_write", "{\"path\":\"/x\"}") },
            new LlmResponse { Content = "understood, stopping", RawResponseBody = null },
        });
        var gate = new ScriptedGate((toolName, mode) =>
            PermissionVerdict.Deny($"tool '{toolName}' is not permitted in mode '{mode}'."));

        var facade = new JsLlmFacade(
            engine, provider, CancellationToken.None, new IBaseTool[] { tool }, budget: null, permissionGate: gate);

        var result = await facade.act("write something", null);

        Assert.Equal("understood, stopping", result.Get("output").AsString());
        Assert.Equal(0, tool.CallCount); // the tool was never executed
        Assert.Equal("default", gate.LastMode); // no permissionMode option → "default"
        // The refusal came back to the model as the tool result of turn 2.
        var turn2 = provider.MessagesPerCall[1];
        Assert.Contains(turn2, m => m.Role == "user" && m.Content.Contains("DENIED:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Allow_executes_the_tool_and_passes_the_act_permissionMode_option()
    {
        using var engine = new Engine();
        var tool = new RecordingTool("file_read");
        var provider = new MessageCapturingProvider(new[]
        {
            new LlmResponse { Content = "", RawResponseBody = ToolCallBody("file_read", "{\"path\":\"/x\"}") },
            new LlmResponse { Content = "done", RawResponseBody = null },
        });
        var gate = new ScriptedGate((_, _) => PermissionVerdict.Allow());

        var facade = new JsLlmFacade(
            engine, provider, CancellationToken.None, new IBaseTool[] { tool }, budget: null, permissionGate: gate);

        var options = EvalOptions(engine, "({ permissionMode: \"acceptEdits\" })");
        var result = await facade.act("read something", options);

        Assert.Equal("done", result.Get("output").AsString());
        Assert.Equal(1, tool.CallCount);
        Assert.Equal("acceptEdits", gate.LastMode);
    }

    [Fact]
    public async Task Denied_calls_consume_no_tool_call_budget()
    {
        using var engine = new Engine();
        var tool = new RecordingTool("file_write");
        var provider = new MessageCapturingProvider(new[]
        {
            new LlmResponse { Content = "", RawResponseBody = ToolCallBody("file_write", "{}") },
            new LlmResponse { Content = "ok", RawResponseBody = null },
        });
        var gate = new ScriptedGate((_, _) => PermissionVerdict.Deny("nope"));
        var budget = new Orkeon.Domain.Autonomous.AgentExecutionBudget { MaxToolCalls = 5 };

        var facade = new JsLlmFacade(
            engine, provider, CancellationToken.None, new IBaseTool[] { tool }, budget, gate);

        await facade.act("write", null);

        Assert.Equal(0, budget.CurrentToolCalls);
    }

    [Fact]
    public async Task Declared_tool_access_is_forwarded_to_the_gate()
    {
        using var engine = new Engine();
        var tool = new RecordingTool("my_probe", ToolAccess.Read);
        var provider = new MessageCapturingProvider(new[]
        {
            new LlmResponse { Content = "", RawResponseBody = ToolCallBody("my_probe", "{}") },
            new LlmResponse { Content = "done", RawResponseBody = null },
        });
        var gate = new ScriptedGate((_, _) => PermissionVerdict.Allow());

        var facade = new JsLlmFacade(
            engine, provider, CancellationToken.None, new IBaseTool[] { tool }, budget: null, permissionGate: gate);

        await facade.act("probe", null);

        Assert.Equal(ToolAccess.Read, gate.LastDeclaredAccess);
    }

    [Fact]
    public async Task Unresolved_tool_reaches_the_gate_as_Unspecified()
    {
        using var engine = new Engine();
        var provider = new MessageCapturingProvider(new[]
        {
            new LlmResponse { Content = "", RawResponseBody = ToolCallBody("ghost_tool", "{}") },
            new LlmResponse { Content = "done", RawResponseBody = null },
        });
        var gate = new ScriptedGate((_, _) => PermissionVerdict.Allow());

        var facade = new JsLlmFacade(
            engine, provider, CancellationToken.None, Array.Empty<IBaseTool>(), budget: null, permissionGate: gate);

        var result = await facade.act("probe", null);

        Assert.Equal("done", result.Get("output").AsString());
        Assert.Equal(ToolAccess.Unspecified, gate.LastDeclaredAccess);
        // The gate allowed it, but the tool does not exist: the loop feeds an ERROR back.
        var turn2 = provider.MessagesPerCall[1];
        Assert.Contains(turn2, m => m.Role == "user" && m.Content.Contains("ERROR:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Null_gate_keeps_the_ungated_behaviour()
    {
        using var engine = new Engine();
        var tool = new RecordingTool("file_write");
        var provider = new MessageCapturingProvider(new[]
        {
            new LlmResponse { Content = "", RawResponseBody = ToolCallBody("file_write", "{}") },
            new LlmResponse { Content = "done", RawResponseBody = null },
        });

        var facade = new JsLlmFacade(engine, provider, CancellationToken.None, new IBaseTool[] { tool });

        var result = await facade.act("write", null);

        Assert.Equal("done", result.Get("output").AsString());
        Assert.Equal(1, tool.CallCount);
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
        public LlmConfig? BaseConfig => LlmConfig.Default() with { Model = "fake-model" };

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

        public Task<ToolCallResponse> CallAsync(ProtocolToolCallRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new ToolCallResponse(true, new Dictionary<string, object?> { ["ok"] = true }, null));
        }

        public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
            => Task.FromResult(ToolResult.CreateSuccess("ok"));

        public bool ValidateInput(string input) => true;
    }
}
