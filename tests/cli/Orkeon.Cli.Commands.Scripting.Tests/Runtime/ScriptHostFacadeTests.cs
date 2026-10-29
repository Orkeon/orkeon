using Orkeon.Tests.Shared.Timing;
using System.Collections.Immutable;
using Jint;
using Jint.Native;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Cli.Commands.Scripting.Dispatch;
using Orkeon.Cli.Commands.Scripting.Runtime;
using Orkeon.Cli.Commands.Scripting.Streaming;
using Orkeon.Cli.Commands.Scripting.Tests.Fixtures;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Infrastructure.Communication;
using Orkeon.Scripting;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Cli.Commands.Scripting.Tests.Runtime;

/// <summary>
/// The <see cref="ScriptHostFacade"/> launches a <c>crew.ork.ts</c> by name,
/// passes it a <c>globalThis.inputs</c> object, and reports the run result. Covers the sync
/// <c>runCrew</c>, the async <c>runCrewAsync</c> (ticket → terminal), <c>listCrews</c>, and
/// error handling. Crews here are plain last-expression scripts (no LLM) so the tests stay
/// hermetic — ScriptHost still returns their value.
/// </summary>
public sealed class ScriptHostFacadeTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Engine _engine = new(options => options.Constraints.PromiseTimeout = HangGuards.EnginePromise);

    public ScriptHostFacadeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "orkeon-script-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        _engine.Dispose();
        foreach (var owned in _owned)
            owned.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    private void WriteCrew(string name, string body)
    {
        var dir = Path.Combine(_tempDir, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "crew.ork.ts"), body);
    }

    private ScriptHostFacade BuildFacade(CommandDispatchService? dispatch = null, TimeSpan? runCrewTimeout = null, IBaseTool? tool = null)
    {
        var fs = new DiskBackedFileSystemService(_tempDir, virtualRoot: "/crews");
        var host = new ScriptHost(fs, new JsEngineFactory(builtInTools: tool is null ? null : [tool]), NullLogger<ScriptHost>.Instance);
        var facadeOptions = new ScriptHostFacadeOptions
        {
            CrewDirectories = ImmutableArray.Create("/crews"),
        };
        if (runCrewTimeout is { } timeout)
            facadeOptions.RunCrewTimeout = timeout;
        var options = Microsoft.Extensions.Options.Options.Create(facadeOptions);
        return new ScriptHostFacade(host, fs, options, dispatch, NullLogger<ScriptHostFacade>.Instance);
    }

    private JsValue JsObject(string literal) => _engine.Evaluate("(" + literal + ")");

    private static CommandDispatchService NewDispatch() => new(
        new InMemoryAgentChannel(NullLogger<InMemoryAgentChannel>.Instance),
        new AgentCommandDirectory(),
        new CommandInstanceRegistry(),
        NullLogger<CommandDispatchService>.Instance);

    [Fact]
    public async Task RunCrew_executes_body_and_returns_output()
    {
        WriteCrew("echo", "\"hello-from-crew\";");
        var facade = BuildFacade();

        var result = facade.runCrew("echo");

        Assert.True(result.ok);
        Assert.Equal("hello-from-crew", result.summary);
        Assert.Null(result.error);
        await Task.CompletedTask;
    }

    [Fact]
    public void RunCrew_passes_inputs_global_to_the_crew()
    {
        WriteCrew("echo", "\"echoed:\" + globalThis.inputs.msg;");
        var facade = BuildFacade();

        var result = facade.runCrew("echo", JsObject("{ msg: 'hi' }"));

        Assert.True(result.ok);
        Assert.Equal("echoed:hi", result.summary);
    }

    [Fact]
    public void RunCrew_unknown_crew_returns_failure_without_throwing()
    {
        var facade = BuildFacade();

        var result = facade.runCrew("does-not-exist");

        Assert.False(result.ok);
        Assert.NotNull(result.error);
        Assert.Contains("not found", result.error!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("a/b")]
    [InlineData("")]
    public void RunCrew_rejects_unsafe_names(string name)
    {
        var facade = BuildFacade();

        var result = facade.runCrew(name);

        Assert.False(result.ok);
        Assert.NotNull(result.error);
    }

    [Fact]
    public void ListCrews_returns_discovered_crew_names()
    {
        WriteCrew("alpha", "1;");
        WriteCrew("beta", "2;");
        var facade = BuildFacade();

        var crews = facade.listCrews();

        Assert.Contains("alpha", crews);
        Assert.Contains("beta", crews);
    }

    [Fact]
    public void RunCrew_timeout_defaults_to_ten_minutes()
    {
        // R10.10 (ANT-007): generous default so legitimate short crews never trip it.
        Assert.Equal(TimeSpan.FromMinutes(10), new ScriptHostFacadeOptions().RunCrewTimeout);
    }

    [Fact]
    public void RunCrew_releases_the_caller_with_a_clear_timeout_when_the_crew_never_finishes()
    {
        // R10.10 (ANT-007): a crew that does not finish must not freeze the REPL. The crew
        // busy-loops for 3 s — far beyond the 150 ms bound configured below, yet
        // self-terminating, so the abandoned pool thread never outlives the test run.
        WriteCrew("hang", "var end = Date.now() + 3000; while (Date.now() < end) { } 'late';");
        var facade = BuildFacade(runCrewTimeout: TimeSpan.FromMilliseconds(150));

        var ex = Assert.Throws<TimeoutException>(() => facade.runCrew("hang"));

        Assert.Contains("hang", ex.Message, StringComparison.Ordinal);
        Assert.Contains("runCrewAsync", ex.Message, StringComparison.Ordinal);
        Assert.Contains("RunCrewTimeout", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RunCrew_within_timeout_still_returns_the_output()
    {
        // Guard against spurious timeouts: a fast crew under a comfortable bound completes.
        WriteCrew("quick", "\"prompt-output\";");
        var facade = BuildFacade(runCrewTimeout: TimeSpan.FromSeconds(30));

        var result = facade.runCrew("quick");

        Assert.True(result.ok);
        Assert.Equal("prompt-output", result.summary);
    }

    /// <summary>A tool whose call streams one turn of <paramref name="text"/> to <paramref name="sink"/>, in the caller's flow.</summary>
    private static StubBaseTool StreamingTool(ConsoleLlmDeltaSink sink, string? label, string text)
        => new StubBaseTool("stream_turn").RespondWith(_ =>
        {
            var turn = sink.BeginTurn(label);
            turn.OnDelta(text);
            turn.OnTurnCompleted();
            return new ToolCallResponse(true, "ok", null);
        });

    [Fact]
    public void RunCrew_reports_streamed_when_a_turn_of_the_crew_reached_the_console_unprefixed()
    {
        WriteCrew("talk", "tools.streamTurn({}); \"the answer\";");
        var console = new ScriptedTestConsole();
        var facade = BuildFacade(tool: StreamingTool(new ConsoleLlmDeltaSink(console), label: null, "the answer"));

        var result = facade.runCrew("talk");

        Assert.True(result.ok, result.error);
        Assert.Equal("the answer", result.summary);
        Assert.True(result.streamed);
        Assert.StartsWith("the answer", console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void RunCrew_reports_not_streamed_when_the_crew_turns_are_labelled()
    {
        WriteCrew("talk", "tools.streamTurn({}); \"the answer\";");
        var console = new ScriptedTestConsole();
        var facade = BuildFacade(tool: StreamingTool(new ConsoleLlmDeltaSink(console), label: "write", "the answer"));

        var result = facade.runCrew("talk");

        Assert.True(result.ok, result.error);
        Assert.False(result.streamed);
        Assert.Contains("[write] the answer", console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void RunCrew_reports_not_streamed_when_nothing_streamed()
    {
        WriteCrew("echo", "\"hello-from-crew\";");
        var facade = BuildFacade();

        var result = facade.runCrew("echo");

        Assert.True(result.ok);
        Assert.False(result.streamed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MapResult_carries_streamed_onto_every_result_shape(bool streamed)
    {
        Assert.Equal(streamed, ScriptHostFacade.MapResult(null, streamed).streamed);
        Assert.Equal(streamed, ScriptHostFacade.MapResult("text", streamed).streamed);
        Assert.Equal(streamed, ScriptHostFacade.MapResult(new { finalOutput = "done" }, streamed).streamed);
    }

    [Fact]
    public async Task RunCrewAsync_completes_with_streamed_false_even_when_a_crew_turn_streamed()
    {
        WriteCrew("talk", "tools.streamTurn({}); \"the answer\";");
        var dispatch = NewDispatch();
        var facade = BuildFacade(dispatch, tool: StreamingTool(new ConsoleLlmDeltaSink(new ScriptedTestConsole()), label: null, "the answer"));

        string ticket;
        using (dispatch.BeginCommand("assistant", CancellationToken.None))
        {
            ticket = facade.runCrewAsync("talk");
        }

        var instance = dispatch.Registry.Get(ticket);
        Assert.NotNull(instance);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (!instance!.IsTerminal && DateTime.UtcNow < deadline)
            await Task.Delay(25, TestContext.Current.CancellationToken);

        Assert.True(instance.IsTerminal, "runCrewAsync instance never reached a terminal state.");
        var view = instance.Snapshot();
        Assert.Equal("done", view.state);
        Assert.Equal("the answer", view.result?.payload);
        Assert.False(view.result?.streamed);
    }

    [Fact]
    public void RunCrewAsync_without_dispatch_throws()
    {
        WriteCrew("echo", "\"x\";");
        var facade = BuildFacade(dispatch: null);

        Assert.Throws<InvalidOperationException>(() => facade.runCrewAsync("echo"));
    }

    [Fact]
    public async Task RunCrewAsync_returns_ticket_and_completes_with_summary()
    {
        WriteCrew("echo", "\"echoed:\" + globalThis.inputs.msg;");
        var dispatch = NewDispatch();
        var facade = BuildFacade(dispatch);

        string ticket;
        using (dispatch.BeginCommand("assistant", CancellationToken.None))
        {
            ticket = facade.runCrewAsync("echo", JsObject("{ msg: 'async' }"));
        }

        Assert.False(string.IsNullOrEmpty(ticket));

        // Drain to terminal — the run executes on a pool thread.
        var instance = dispatch.Registry.Get(ticket);
        Assert.NotNull(instance);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (!instance!.IsTerminal && DateTime.UtcNow < deadline)
            await Task.Delay(25, TestContext.Current.CancellationToken);

        Assert.True(instance.IsTerminal, "runCrewAsync instance never reached a terminal state.");
        var view = instance.Snapshot();
        Assert.Equal("done", view.state);
        Assert.Equal("echoed:async", view.result?.payload);
    }

    [Fact]
    [Trait("BR", "#239")]
    public async Task RunCrewAsync_exclusive_refuses_a_crew_still_running_naming_its_ticket_then_admits_it_once_done()
    {
        WriteCrew("review", "tools.hold({}); \"reviewed\";");
        using var held = new ManualResetEventSlim();
        var hold = new StubBaseTool("hold").RespondWith(_ =>
        {
            held.Wait(TimeSpan.FromSeconds(15));
            return new ToolCallResponse(true, "ok", null);
        });
        var dispatch = NewDispatch();
        var facade = BuildFacade(dispatch, tool: hold);

        string first;
        Jint.Runtime.JavaScriptException refused;
        using (dispatch.BeginCommand("review", CancellationToken.None))
        {
            first = facade.runCrewAsync("review", options: JsObject("{ exclusive: true }"));
            refused = Assert.Throws<Jint.Runtime.JavaScriptException>(
                () => facade.runCrewAsync("review", options: JsObject("{ exclusive: true }")));
        }

        Assert.Equal($"crew 'review' already running (ticket {first})", refused.Message);
        Assert.Single(dispatch.Registry.List(CommandInstanceFilter.All));

        held.Set();
        await WaitTerminal(dispatch.Registry.Get(first)!);
        Assert.Equal("done", dispatch.Registry.Get(first)!.Snapshot().state);
        string second;
        using (dispatch.BeginCommand("review", CancellationToken.None))
            second = facade.runCrewAsync("review", options: JsObject("{ exclusive: true }"));
        await WaitTerminal(dispatch.Registry.Get(second)!);
        Assert.Equal("done", dispatch.Registry.Get(second)!.Snapshot().state);
    }

    private static async Task WaitTerminal(CommandInstance instance)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (!instance.IsTerminal && DateTime.UtcNow < deadline)
            await Task.Delay(25, TestContext.Current.CancellationToken);
        Assert.True(instance.IsTerminal, $"instance {instance.Ticket} never reached a terminal state.");
    }

    [Fact]
    [Trait("BR", "#239")]
    public void RunCrewAsync_exclusive_that_is_not_a_boolean_is_a_script_error()
    {
        var facade = BuildFacade(NewDispatch());

        var refused = Assert.Throws<Jint.Runtime.JavaScriptException>(
            () => facade.runCrewAsync("review", options: JsObject("{ exclusive: 'yes' }")));
        Assert.Equal("script-host.runCrewAsync: exclusive must be a boolean, not 'yes'.", refused.Message);
    }

    // ── runCrew holds the read loop: its wait answers the crew's questions ──

    private static Orkeon.Application.Interfaces.Security.ToolApprovalRequest Question() => new()
    {
        ToolName = "file_write",
        Arguments = ImmutableDictionary<string, object?>.Empty,
        Mode = "default",
        Reason = "tool 'file_write' requires approval (mode 'default').",
    };

    private readonly List<IDisposable> _owned = [];

    // Disposed with the class: a channel outlives the crew run that captured it.
    private T Owned<T>(T disposable) where T : IDisposable
    {
        _owned.Add(disposable);
        return disposable;
    }

    /// <summary>A crew run that asks one question and finishes with the operator's decision.</summary>
    private static Task<string> RunAsking(ConsoleToolApprovalChannel channel) => Task.Run(async () =>
        (await channel.RequestAsync(Question(), CancellationToken.None)).ToString());

    [Fact]
    public void RunCrew_wait_answers_the_question_of_its_crew_from_the_console()
    {
        var console = new Fixtures.ScriptedTestConsole();
        var channel = Owned(new ConsoleToolApprovalChannel(() => console));
        console.EnqueueLine("y");

        var result = ScriptHostFacade.WaitAnsweringApprovals(RunAsking(channel), TimeSpan.FromMinutes(1), channel);

        Assert.Equal("AllowOnce", result);
        Assert.Contains("approve file_write?", console.Output, StringComparison.Ordinal);
        Assert.Contains("file_write allowed once", console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunCrew_wait_does_not_count_the_time_the_operator_takes_to_answer()
    {
        var console = new Fixtures.ScriptedTestConsole();
        var channel = Owned(new ConsoleToolApprovalChannel(() => console));
        var answer = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            console.EnqueueLine("n");
        }, TestContext.Current.CancellationToken);

        // One second of budget, two seconds to answer: only the crew's own time counts.
        var result = ScriptHostFacade.WaitAnsweringApprovals(RunAsking(channel), TimeSpan.FromSeconds(1), channel);

        Assert.Equal("Deny", result);
        await answer;
    }

    [Fact]
    public void RunCrew_wait_still_times_out_when_the_crew_outlives_its_budget_without_asking()
    {
        var console = new Fixtures.ScriptedTestConsole();
        var channel = Owned(new ConsoleToolApprovalChannel(() => console));
        var never = new TaskCompletionSource<string>();

        Assert.Throws<TimeoutException>(() =>
            ScriptHostFacade.WaitAnsweringApprovals(never.Task, TimeSpan.FromMilliseconds(200), channel));
        Assert.Equal("", console.Output);
    }

    [Fact]
    [Trait("BR", "RT-20")]
    public void ShouldRefuseChain_WhenDepthExceedsLimit()
    {
        var dispatch = new CommandDispatchService(
            new InMemoryAgentChannel(NullLogger<InMemoryAgentChannel>.Instance),
            new AgentCommandDirectory(),
            new CommandInstanceRegistry(),
            NullLogger<CommandDispatchService>.Instance,
            maxChainDepth: 2);
        Task<CommandResponse> Work(CancellationToken _) => Task.FromResult(new CommandResponse("a", "i", success: true, payload: "p", error: null));

        using (dispatch.BeginCommand("assistant", CancellationToken.None, parentDepth: 1))
            Assert.False(string.IsNullOrEmpty(dispatch.postWork("below", "crew:x", Work)));

        using var scope = dispatch.BeginCommand("assistant", CancellationToken.None, parentDepth: 2);
        var ex = Assert.Throws<ChainDepthExceededException>(() => dispatch.postWork("at-limit", "crew:x", Work));

        Assert.Equal(2, ex.Depth);
        Assert.Equal(2, ex.Limit);
        Assert.Equal("chain depth 2 reached (MaxChainDepth 2, /assistant)", ex.Message);
    }
}
