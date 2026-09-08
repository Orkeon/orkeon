using System.Collections.Immutable;
using Jint;
using Jint.Native;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Cli.Commands.Scripting.Dispatch;
using Orkeon.Cli.Commands.Scripting.Runtime;
using Orkeon.Infrastructure.Communication;
using Orkeon.Scripting;
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
    private readonly Engine _engine = new();

    public ScriptHostFacadeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "orkeon-script-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        _engine.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    private void WriteCrew(string name, string body)
    {
        var dir = Path.Combine(_tempDir, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "crew.ork.ts"), body);
    }

    private ScriptHostFacade BuildFacade(CommandDispatchService? dispatch = null, TimeSpan? runCrewTimeout = null)
    {
        var fs = new DiskBackedFileSystemService(_tempDir, virtualRoot: "/crews");
        var host = new ScriptHost(fs, new JsEngineFactory(), NullLogger<ScriptHost>.Instance);
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
        using (CommandDispatchService.BeginCommand("assistant", CancellationToken.None))
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
}
