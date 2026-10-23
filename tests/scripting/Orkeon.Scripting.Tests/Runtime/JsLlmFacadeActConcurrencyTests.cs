using Jint;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Infrastructure.Security;
using Orkeon.Scripting.Runtime;
using Orkeon.Scripting.Tests.Doubles;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Scripting.Tests.Runtime;

/// <summary>RT-07: the read-only tool calls of one act turn run concurrently between barriers.</summary>
public sealed class JsLlmFacadeActConcurrencyTests
{
    // A sequential loop never reaches the rendezvous: the wait ends in a fault (turned into an
    // "ERROR:" tool result) instead of hanging the run. Never reached by a concurrent loop.
    private static readonly TimeSpan RendezvousGuard = TimeSpan.FromSeconds(5);

    private sealed class Rendezvous(int parties)
    {
        private readonly TaskCompletionSource _all = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        public Task ArriveAsync()
        {
            if (Interlocked.Increment(ref _arrived) == parties) _all.TrySetResult();
            return _all.Task.WaitAsync(RendezvousGuard);
        }
    }

    private sealed class ScriptedTool(string name, ToolAccess access, List<string> log, Func<string, Task> body, int resultChars = 0, Action<string>? ended = null) : IBaseTool
    {
        private int _active;
        public int MaxActive;

        public string Name { get; } = name;
        public ToolAccess Access { get; } = access;
        public string Description => "test tool";
        public ToolSchema Schema => new(Name, Description, new Dictionary<string, ParameterSchema>());

        public async Task<ToolCallResponse> CallAsync(Orkeon.Domain.Tools.Protocol.ToolCallRequest request, CancellationToken cancellationToken = default)
        {
            var path = request.Parameters["path"]?.ToString() ?? "";
            var now = Interlocked.Increment(ref _active);
            lock (log) { log.Add("start:" + path); MaxActive = Math.Max(MaxActive, now); }
            try
            {
                await body(path).ConfigureAwait(false);
            }
            finally
            {
                lock (log) log.Add("end:" + path);
                Interlocked.Decrement(ref _active);
                ended?.Invoke(path);
            }
            return new ToolCallResponse(true, new Dictionary<string, object?> { ["path"] = resultChars > 0 ? new string('x', resultChars) : path }, null);
        }

        public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
            => Task.FromResult(ToolResult.CreateSuccess("ok"));

        public bool ValidateInput(string input) => true;
    }

    /// <summary>One chat turn asking for several calls (ids c1..cn), then a final answer.</summary>
    private sealed class FakeMultiCallLlmProvider(params (string Tool, string Path)[] calls) : ILlmProvider
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
            var items = calls.Select((c, i) =>
                "{\"id\":\"c" + (i + 1) + "\",\"type\":\"function\",\"function\":{\"name\":\"" + c.Tool + "\",\"arguments\":"
                + System.Text.Json.JsonSerializer.Serialize("{\"path\":\"" + c.Path + "\"}") + "}}");
            return Task.FromResult(new LlmResponse
            {
                Content = "",
                RawResponseBody = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"\",\"tool_calls\":[" + string.Join(",", items) + "]}}]}",
            });
        }
    }

    /// <summary>Delegates to the mode gate; throws for the paths it is told to.</summary>
    private sealed class StubFaultingPermissionGate(HashSet<string> faultingPaths) : IPermissionGate
    {
        private readonly ModePermissionGate _inner = new(new StubToolApprovalChannel());

        public Task<PermissionVerdict> CheckAsync(
            string toolName, IReadOnlyDictionary<string, object?> arguments, string mode,
            ToolAccess declaredAccess = ToolAccess.Unspecified, CancellationToken cancellationToken = default)
        {
            var path = arguments["path"]?.ToString() ?? "";
            if (faultingPaths.Contains(path))
                throw new InvalidOperationException("fault:" + path);
            return _inner.CheckAsync(toolName, arguments, mode, declaredAccess, cancellationToken);
        }
    }

    private static string[] ToolMessageContents(LlmMessage[] turn) => turn.Where(m => m.Role == "tool").Select(m => m.Content).ToArray();

    private static string[] ToolMessageIds(LlmMessage[] turn) => turn.Where(m => m.Role == "tool").Select(m => m.ToolCallId!).ToArray();

    [Fact]
    [Trait("BR", "RT-07")]
    public async Task ShouldReturnThreeFileReadsInOneIteration_WhenActTurnAsksThem()
    {
        using var engine = new Engine(options => options.Constraints.PromiseTimeout = HangGuards.EnginePromise);
        var log = new List<string>();
        var rendezvous = new Rendezvous(3);
        var tool = new ScriptedTool("file_read", ToolAccess.Read, log, _ => rendezvous.ArriveAsync());
        var provider = new FakeMultiCallLlmProvider(("file_read", "a"), ("file_read", "b"), ("file_read", "c"));
        var facade = new JsLlmFacade(engine, provider, CancellationToken.None, new IBaseTool[] { tool }, new JsLlmActGuards());

        var options = await engine.EvaluateAsync("({ permissionMode: 'default' })", null, TestContext.Current.CancellationToken);
        var result = await facade.ActAsync(engine, "read them", options);

        Assert.Equal("final", result.Get("output").AsString());
        Assert.Equal(2, provider.Turns.Count);
        Assert.Equal(3, tool.MaxActive);
        Assert.Equal(["c1", "c2", "c3"], ToolMessageIds(provider.Turns[1]));
        Assert.Equal(["{\"path\":\"a\"}", "{\"path\":\"b\"}", "{\"path\":\"c\"}"], ToolMessageContents(provider.Turns[1]));
    }

    [Fact]
    [Trait("BR", "RT-07")]
    public async Task ShouldRunToolCallbacksInCallOrderOnEngineThread_WhenActReadsFanOut()
    {
        using var engine = new Engine(options => options.Constraints.PromiseTimeout = HangGuards.EnginePromise);
        var log = new List<string>();
        var callbackActive = 0;
        var callbackMaxActive = 0;
        engine.SetValue("__rec", new Action<string>(label =>
        {
            var now = Interlocked.Increment(ref callbackActive);
            lock (log) { log.Add(label); callbackMaxActive = Math.Max(callbackMaxActive, now); }
            Thread.Sleep(5);
            Interlocked.Decrement(ref callbackActive);
        }));

        var rendezvous = new Rendezvous(3);
        var done = new Dictionary<string, TaskCompletionSource>
        {
            ["a"] = new(TaskCreationOptions.RunContinuationsAsynchronously),
            ["b"] = new(TaskCreationOptions.RunContinuationsAsynchronously),
            ["c"] = new(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        // The reads complete in reverse order: c first, then b, then a.
        var next = new Dictionary<string, string?> { ["a"] = "b", ["b"] = "c", ["c"] = null };
        var tool = new ScriptedTool("file_read", ToolAccess.Read, log, async path =>
        {
            await rendezvous.ArriveAsync().ConfigureAwait(false);
            // Released once the dependency's end is recorded in the log, never before.
            if (next[path] is { } dependency)
                await done[dependency].Task.WaitAsync(RendezvousGuard).ConfigureAwait(false);
        }, ended: path => done[path].TrySetResult());
        var provider = new FakeMultiCallLlmProvider(("file_read", "a"), ("file_read", "b"), ("file_read", "c"));
        var facade = new JsLlmFacade(engine, provider, CancellationToken.None, new IBaseTool[] { tool }, new JsLlmActGuards());

        var options = await engine.EvaluateAsync(
            "({ permissionMode: 'default', onToolCall: (c) => { __rec('call:' + c.input.path); return 'allow'; }, onToolResult: (c, r) => { __rec('result:' + c.input.path); } })",
            null, TestContext.Current.CancellationToken);
        await facade.ActAsync(engine, "read them", options);

        string[] snapshot;
        lock (log) snapshot = log.ToArray();
        Assert.Equal(["call:a", "call:b", "call:c"], snapshot.Take(3));
        Assert.Equal(["result:a", "result:b", "result:c"], snapshot.Where(e => e.StartsWith("result:", StringComparison.Ordinal)));
        Assert.Equal(["end:c", "end:b", "end:a"], snapshot.Where(e => e.StartsWith("end:", StringComparison.Ordinal)));
        Assert.Equal(1, callbackMaxActive);
        Assert.Equal(["c1", "c2", "c3"], ToolMessageIds(provider.Turns[1]));
        Assert.Equal(["{\"path\":\"a\"}", "{\"path\":\"b\"}", "{\"path\":\"c\"}"], ToolMessageContents(provider.Turns[1]));
    }

    [Fact]
    [Trait("BR", "RT-07")]
    public async Task ShouldRunReadAfterWriteOnlyOnceWriteEnded_WhenTurnIsReadWriteRead()
    {
        using var engine = new Engine(options => options.Constraints.PromiseTimeout = HangGuards.EnginePromise);
        var log = new List<string>();
        var written = false;
        var seenByLaterReads = new List<bool>();
        var before = new Rendezvous(2);
        var after = new Rendezvous(2);
        var read = new ScriptedTool("file_read", ToolAccess.Read, log, async path =>
        {
            if (path is "a" or "b")
            {
                await before.ArriveAsync().ConfigureAwait(false);
                return;
            }
            lock (seenByLaterReads) seenByLaterReads.Add(written);
            await after.ArriveAsync().ConfigureAwait(false);
        });
        var write = new ScriptedTool("file_write", ToolAccess.Edit, log, async _ =>
        {
            await Task.Delay(20).ConfigureAwait(false);
            written = true;
        });
        var provider = new FakeMultiCallLlmProvider(
            ("file_read", "a"), ("file_read", "b"), ("file_write", "w"), ("file_read", "c"), ("file_read", "d"));
        var facade = new JsLlmFacade(engine, provider, CancellationToken.None, new IBaseTool[] { read, write }, new JsLlmActGuards());

        var options = await engine.EvaluateAsync("({ permissionMode: 'default' })", null, TestContext.Current.CancellationToken);
        await facade.ActAsync(engine, "read, write, read", options);

        string[] snapshot;
        lock (log) snapshot = log.ToArray();
        var endWrite = Array.IndexOf(snapshot, "end:w");
        Assert.True(endWrite >= 0);
        Assert.True(Array.IndexOf(snapshot, "start:c") > endWrite);
        Assert.True(Array.IndexOf(snapshot, "start:d") > endWrite);
        Assert.True(Array.IndexOf(snapshot, "end:a") < Array.IndexOf(snapshot, "start:w"));
        Assert.True(Array.IndexOf(snapshot, "end:b") < Array.IndexOf(snapshot, "start:w"));
        Assert.Equal([true, true], seenByLaterReads);
        Assert.Equal(["c1", "c2", "c3", "c4", "c5"], ToolMessageIds(provider.Turns[1]));
        Assert.Equal(
            ["{\"path\":\"a\"}", "{\"path\":\"b\"}", "{\"path\":\"w\"}", "{\"path\":\"c\"}", "{\"path\":\"d\"}"],
            ToolMessageContents(provider.Turns[1]));
    }

    [Fact]
    [Trait("BR", "RT-07")]
    public async Task ShouldCompleteSiblingsAndRethrowFirstFault_WhenOneParallelReadFaults()
    {
        using var engine = new Engine(options => options.Constraints.PromiseTimeout = HangGuards.EnginePromise);
        var log = new List<string>();
        var tool = new ScriptedTool("file_read", ToolAccess.Read, log, _ => Task.CompletedTask);
        var provider = new FakeMultiCallLlmProvider(("file_read", "a"), ("file_read", "b"), ("file_read", "c"));
        var gate = new StubFaultingPermissionGate(["a", "c"]);
        var facade = new JsLlmFacade(
            engine, provider, CancellationToken.None, new IBaseTool[] { tool }, new JsLlmActGuards { PermissionGate = gate });

        var options = await engine.EvaluateAsync("({ permissionMode: 'default' })", null, TestContext.Current.CancellationToken);
        var fault = await Assert.ThrowsAsync<InvalidOperationException>(() => facade.ActAsync(engine, "read them", options));

        Assert.Equal("fault:a", fault.Message);
        string[] snapshot;
        lock (log) snapshot = log.ToArray();
        Assert.Equal(["start:b", "end:b"], snapshot);
    }

    [Fact]
    [Trait("BR", "RT-10")]
    public async Task ShouldCutActToolResultOnConfiguredBound_WhenLimitSetForTool()
    {
        // The serialised result is {"path":"xxx..."}: 11 characters around the payload.
        const int payload = 60000;
        const int serialised = payload + 11;

        async Task<string> RunAsync(int limit)
        {
            using var engine = new Engine(options => options.Constraints.PromiseTimeout = HangGuards.EnginePromise);
            var tool = new ScriptedTool("big_read", ToolAccess.Read, new List<string>(), _ => Task.CompletedTask, payload);
            var provider = new FakeMultiCallLlmProvider(("big_read", "a"));
            var limits = new ToolResultLimitsOptions();
            limits.Limits["big_read"] = limit;
            var facade = new JsLlmFacade(engine, provider, CancellationToken.None, new IBaseTool[] { tool }, new JsLlmActGuards())
            {
                ToolLimits = limits,
            };
            var options = await engine.EvaluateAsync("({ permissionMode: 'default' })", null, TestContext.Current.CancellationToken);
            await facade.ActAsync(engine, "read it", options);
            return Assert.Single(ToolMessageContents(provider.Turns[1]));
        }

        var raised = await RunAsync(100_000);
        Assert.Equal(serialised, raised.Length);
        Assert.DoesNotContain("truncated", raised);

        var lowered = await RunAsync(100);
        Assert.EndsWith(
            "\n\n[... truncated, " + (serialised - 100) + " chars omitted. Use more specific parameters to narrow results.]",
            lowered);
        Assert.Equal(100, lowered.IndexOf("\n\n[... truncated", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("BR", "RT-07")]
    public async Task ShouldBoundActFanOutBySetting_WhenMaxParallelToolCallsIsTwo()
    {
        using var engine = new Engine(options => options.Constraints.PromiseTimeout = HangGuards.EnginePromise);
        var log = new List<string>();
        var pair = new Rendezvous(2);
        var tool = new ScriptedTool("file_read", ToolAccess.Read, log, _ => pair.ArriveAsync());
        var provider = new FakeMultiCallLlmProvider(
            ("file_read", "a"), ("file_read", "b"), ("file_read", "c"),
            ("file_read", "d"), ("file_read", "e"), ("file_read", "f"));
        var limits = new ToolResultLimitsOptions { MaxParallelToolCalls = 2 };
        var facade = new JsLlmFacade(engine, provider, CancellationToken.None, new IBaseTool[] { tool }, new JsLlmActGuards())
        {
            ToolLimits = limits,
        };

        var options = await engine.EvaluateAsync("({ permissionMode: 'default' })", null, TestContext.Current.CancellationToken);
        await facade.ActAsync(engine, "read them", options);

        Assert.Equal(2, tool.MaxActive);
        Assert.Equal(6, ToolMessageIds(provider.Turns[1]).Length);
    }
}
