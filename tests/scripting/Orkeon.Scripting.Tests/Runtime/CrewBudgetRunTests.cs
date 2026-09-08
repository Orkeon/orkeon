using Orkeon.Domain.Autonomous;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Scripting.Runtime;
using ProtocolToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;

namespace Orkeon.Scripting.Tests.Runtime;

/// <summary>
/// Budget enforcement end-to-end (the test twin of an internal acceptance probe): a crew declaring
/// <c>.budget({ toolCalls: 1 })</c> whose agent body drives a divergent
/// <c>ctx.llm.act</c> loop must surface a typed <see cref="BudgetExhaustedException"/>
/// from <c>crew.RunAsync</c> — not a generic promise rejection, and not a normal
/// completion after N tool calls (the pre-F1 failure mode).
/// </summary>
public sealed class CrewBudgetRunTests
{
    private static string ToolCallBody(string name, string argsJson)
    {
        var argsEncoded = System.Text.Json.JsonSerializer.Serialize(argsJson);
        return "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"\",\"tool_calls\":[" +
               "{\"id\":\"c1\",\"type\":\"function\",\"function\":{\"name\":\"" + name + "\",\"arguments\":" + argsEncoded + "}}]}}]}";
    }

    private static JsCrew BuildCrew(Jint.Engine engine, string script)
        => (JsCrew)engine.Evaluate(script).ToObject()!;

    [Fact]
    public async Task Crew_run_with_toolCalls_budget_1_surfaces_typed_BudgetExhaustedException()
    {
        var tool = new CountingTool("echo_tool");
        var provider = new LoopingProvider(ToolCallBody("echo_tool", "{\"text\":\"again\"}"));
        var factory = new Orkeon.Scripting.JsEngineFactory(
            builtInTools: new IBaseTool[] { tool },
            llmProvider: provider);
        using var engine = factory.Create();

        var crew = BuildCrew(engine, """
            const a = agentBuilder()
              .name("A").role("R").goal("G")
              .tools(["echo_tool"])
              .body(async (_input, ctx) => {
                const r = await ctx.llm.act("loop forever", { maxIterations: 40 });
                return String(r?.output ?? r);
              })
              .build();
            crewBuilder().name("c").budget({ toolCalls: 1 }).withAgent(a).build();
            """);

        var ex = await Assert.ThrowsAsync<BudgetExhaustedException>(
            () => crew.RunAsync(null, CancellationToken.None));

        Assert.Equal(BudgetDimension.ToolCalls, ex.Dimension);
        Assert.Equal(1, tool.CallCount);
    }

    [Fact]
    public async Task Crew_run_without_budget_keeps_legacy_behaviour()
    {
        var tool = new CountingTool("echo_tool");
        var provider = new LoopingProvider(ToolCallBody("echo_tool", "{\"text\":\"again\"}"));
        var factory = new Orkeon.Scripting.JsEngineFactory(
            builtInTools: new IBaseTool[] { tool },
            llmProvider: provider);
        using var engine = factory.Create();

        var crew = BuildCrew(engine, """
            const a = agentBuilder()
              .name("A").role("R").goal("G")
              .tools(["echo_tool"])
              .body(async (_input, ctx) => {
                const r = await ctx.llm.act("loop forever", { maxIterations: 3 });
                return String(r?.output ?? r);
              })
              .build();
            crewBuilder().name("c").withAgent(a).build();
            """);

        // No budget → the only bound is maxIterations; the run terminates normally.
        var result = await crew.RunAsync(null, CancellationToken.None);

        Assert.Equal(3, tool.CallCount);
        Assert.Contains("max tool-call iterations", result.output, StringComparison.Ordinal);
    }

    // ── fakes ────────────────────────────────────────────────────────────────

    /// <summary>Always answers with the same tool-call body: a divergent model.</summary>
    private sealed class LoopingProvider : ILlmProvider
    {
        private readonly string _body;
        public LoopingProvider(string body) => _body = body;
        public string Name => "fake";
        public LlmConfig? BaseConfig => LlmConfig.Default() with { Model = "fake-model" };

        public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken ct = default)
            => Task.FromResult(new LlmResponse { Content = "", RawResponseBody = _body });

        public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken ct = default)
            => Task.FromResult(new LlmResponse { Content = "", RawResponseBody = _body });
    }

    private sealed class CountingTool : IBaseTool
    {
        public CountingTool(string name) => Name = name;
        public string Name { get; }
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
