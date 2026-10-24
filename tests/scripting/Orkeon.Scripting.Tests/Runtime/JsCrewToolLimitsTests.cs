using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Scripting.Runtime;

namespace Orkeon.Scripting.Tests.Runtime;

/// <summary>R22: the host's ToolResultLimitsOptions apply to ctx.llm.act of a crew run through a script.</summary>
public sealed class JsCrewToolLimitsTests
{
    private static readonly TimeSpan Safety = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RendezvousGuard = TimeSpan.FromSeconds(1);

    private const string Js = """
        const a = agentBuilder().name("a").role("R").goal("G").tools(["file_read"])
            .body(async (input, ctx) => (await ctx.llm.act("go", { permissionMode: "bypassPermissions" })).output)
            .build();
        crewBuilder().name("c").withAgent(a).build();
        """;

    private sealed class ReadTool(int resultChars, int parties) : IBaseTool
    {
        private readonly TaskCompletionSource _all = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;
        private readonly object _sync = new();
        private int _active;
        public int MaxActive;

        public string Name => "file_read";
        public ToolAccess Access => ToolAccess.Read;
        public string Description => "test tool";
        public ToolSchema Schema => new(Name, Description, new Dictionary<string, ParameterSchema>());

        public async Task<ToolCallResponse> CallAsync(Orkeon.Domain.Tools.Protocol.ToolCallRequest request, CancellationToken cancellationToken = default)
        {
            var now = Interlocked.Increment(ref _active);
            lock (_sync) MaxActive = Math.Max(MaxActive, now);
            try
            {
                if (parties > 0)
                {
                    if (Interlocked.Increment(ref _arrived) == parties) _all.TrySetResult();
                    // A third concurrent call ends the wait at once; a bound of two lets the guard expire instead.
                    try { await _all.Task.WaitAsync(RendezvousGuard, cancellationToken).ConfigureAwait(false); }
                    catch (TimeoutException) { }
                }
            }
            finally { Interlocked.Decrement(ref _active); }
            return new ToolCallResponse(true, new Dictionary<string, object?> { ["path"] = new string('x', resultChars) }, null);
        }

        public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
            => Task.FromResult(ToolResult.CreateSuccess("ok"));

        public bool ValidateInput(string input) => true;
    }

    /// <summary>One chat turn asking for <c>calls</c> reads (ids c1..cn), then a final answer.</summary>
    private sealed class FakeMultiCallLlmProvider(int calls) : ILlmProvider
    {
        private int _turn;
        public List<LlmMessage[]> Turns { get; } = new();

        public string Name => "fake-multi";
        public LlmConfig? BaseConfig => LlmConfig.Create("fake-multi-model");

        public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new LlmResponse { Content = prompt });

        public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
        {
            Turns.Add(messages);
            if (_turn++ > 0)
                return Task.FromResult(new LlmResponse { Content = "final" });
            var items = Enumerable.Range(1, calls).Select(i =>
                "{\"id\":\"c" + i + "\",\"type\":\"function\",\"function\":{\"name\":\"file_read\",\"arguments\":"
                + System.Text.Json.JsonSerializer.Serialize("{\"path\":\"p" + i + "\"}") + "}}");
            return Task.FromResult(new LlmResponse
            {
                Content = "",
                RawResponseBody = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"\",\"tool_calls\":[" + string.Join(",", items) + "]}}]}",
            });
        }
    }

    private static async Task RunAsync(IBaseTool tool, ILlmProvider provider, ToolResultLimitsOptions limits)
    {
        var ports = new ScriptingHostPorts { ToolLimits = limits };
        var engine = new JsEngineFactory(builtInTools: [tool], llmProvider: provider, hostPorts: ports).Create();
        var crew = (JsCrew)(await engine.EvaluateAsync(Js, null, TestContext.Current.CancellationToken)).ToObject()!;
        await crew.RunAsync(null, TestContext.Current.CancellationToken).WaitAsync(Safety, TestContext.Current.CancellationToken);
    }

    [Fact]
    [Trait("BR", "R22")]
    public async Task ShouldCutToolResult_WhenHostToolLimitsSetALowerLimit()
    {
        var provider = new FakeMultiCallLlmProvider(1);
        var limits = new ToolResultLimitsOptions();
        limits.Limits["file_read"] = 10;

        await RunAsync(new ReadTool(100, 0), provider, limits);

        var seen = Assert.Single(provider.Turns[1], m => m.Role == "tool").Content;
        Assert.Equal(ToolResultLimitsOptions.Cut("{\"path\":\"" + new string('x', 100) + "\"}", 10), seen);
    }

    [Fact]
    [Trait("BR", "R22")]
    public async Task ShouldBoundParallelReads_WhenHostSetsMaxParallelToolCalls()
    {
        var provider = new FakeMultiCallLlmProvider(4);
        var tool = new ReadTool(1, 3);

        await RunAsync(tool, provider, new ToolResultLimitsOptions { MaxParallelToolCalls = 2 });

        Assert.Equal(4, provider.Turns[1].Count(m => m.Role == "tool"));
        Assert.Equal(2, tool.MaxActive);
    }
}
