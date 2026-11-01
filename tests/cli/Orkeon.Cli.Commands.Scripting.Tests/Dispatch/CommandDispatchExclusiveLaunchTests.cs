using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Cli.Commands.Scripting.Dispatch;
using Orkeon.Cli.Commands.Scripting.Runtime;
using Orkeon.Cli.Commands.Scripting.Tests.Fixtures;
using Orkeon.Infrastructure.Communication;
using Orkeon.Scripting;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Cli.Commands.Scripting.Tests.Dispatch;

/// <summary>
/// #239: <c>postWork(..., exclusive: true)</c> refuses a target whose previous exclusive launch is not terminal, in the
/// same step as the registration of the instance.
/// </summary>
[Collection("PromptScope")]
public sealed class CommandDispatchExclusiveLaunchTests : IDisposable
{
    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "orkeon-exclusive-" + Guid.NewGuid().ToString("N"));
    private readonly string _crewsDir;

    public CommandDispatchExclusiveLaunchTests()
    {
        Directory.CreateDirectory(_tempDir);
        _crewsDir = _tempDir + "-crews";
        Directory.CreateDirectory(_crewsDir);
    }

    private readonly TaskCompletionSource _stubbornGate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Work that ignores its cancellation token: it ends only when the test opens the gate.</summary>
    private async Task<CommandResponse> Stubborn(CancellationToken _)
    {
        await _stubbornGate.Task.ConfigureAwait(false);
        return new CommandResponse("a", "i", success: true, payload: "p", error: null);
    }

    private static Jint.Native.JsValue Options(string literal)
    {
        using var engine = new Jint.Engine(options => options.Constraints.PromiseTimeout = Orkeon.Tests.Shared.Timing.HangGuards.EnginePromise);
        return engine.Evaluate(literal);
    }

    private ScriptHostFacade NewFacade(CommandDispatchService service)
    {
        Directory.CreateDirectory(Path.Combine(_crewsDir, "one"));
        File.WriteAllText(Path.Combine(_crewsDir, "one", "crew.ork.ts"), "\"one-out\";");
        var crewFs = new DiskBackedFileSystemService(_crewsDir, virtualRoot: "/crews");
        return new ScriptHostFacade(
            new ScriptHost(crewFs, new JsEngineFactory(), NullLogger<ScriptHost>.Instance),
            crewFs,
            Microsoft.Extensions.Options.Options.Create(new ScriptHostFacadeOptions { CrewDirectories = ImmutableArray.Create("/crews") }),
            service,
            NullLogger<ScriptHostFacade>.Instance);
    }

    public void Dispose()
    {
        _gate.TrySetResult();
        _stubbornGate.TrySetResult();
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
        try { Directory.Delete(_crewsDir, recursive: true); } catch { /* best effort */ }
    }

    private static CommandDispatchService NewService() => new(
        new InMemoryAgentChannel(NullLogger<InMemoryAgentChannel>.Instance),
        new AgentCommandDirectory(),
        new CommandInstanceRegistry(),
        NullLogger<CommandDispatchService>.Instance);

    private async Task<CommandResponse> Gated(CancellationToken ct)
    {
        await _gate.Task.WaitAsync(ct).ConfigureAwait(false);
        return new CommandResponse("a", "i", success: true, payload: "p", error: null);
    }

    private static Task<CommandResponse> Instant(CancellationToken _)
        => Task.FromResult(new CommandResponse("a", "i", success: true, payload: "p", error: null));

    private static async Task WaitTerminal(CommandInstance instance)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (!instance.IsTerminal && DateTime.UtcNow < deadline)
            await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(instance.IsTerminal, $"instance {instance.Ticket} never reached a terminal state.");
    }

    [Fact]
    [Trait("BR", "#239")]
    public async Task ShouldRefuseTheSecondLaunchWithTheFirstTicket_WhenTheFirstStillRuns()
    {
        var service = NewService();

        var first = service.postWork("review", "crew:review", Gated, exclusive: true);
        var refused = Assert.Throws<ExclusiveLaunchRefusedException>(() => service.postWork("review", "crew:review", Gated, exclusive: true));

        Assert.Equal(first, refused.Ticket);
        Assert.Equal($"crew:review already running (ticket {first})", refused.Message);
        Assert.Single(service.Registry.List(CommandInstanceFilter.All));
        _gate.SetResult();
        await WaitTerminal(service.Registry.Get(first)!);
    }

    [Fact]
    [Trait("BR", "#239")]
    public async Task ShouldAdmitANewLaunch_WhenTheHolderCompleted()
    {
        var service = NewService();
        var first = service.postWork("review", "crew:review", Instant, exclusive: true);
        await WaitTerminal(service.Registry.Get(first)!);

        var second = service.postWork("review", "crew:review", Instant, exclusive: true);

        Assert.NotEqual(first, second);
        await WaitTerminal(service.Registry.Get(second)!);
    }

    [Fact]
    [Trait("BR", "#239")]
    public async Task ShouldNeitherRefuseNorCount_ALaunchWithoutTheFlagOrOfAnotherTarget()
    {
        var service = NewService();
        var first = service.postWork("review", "crew:review", Gated, exclusive: true);

        var plain = service.postWork("review", "crew:review", Instant);
        var other = service.postWork("index", "crew:index", Instant, exclusive: true);

        Assert.Equal(3, service.Registry.List(CommandInstanceFilter.All).Count);
        await WaitTerminal(service.Registry.Get(plain)!);
        await WaitTerminal(service.Registry.Get(other)!);
        _gate.SetResult();
        await WaitTerminal(service.Registry.Get(first)!);
    }

    [Fact]
    [Trait("BR", "#239")]
    public async Task ShouldAdmitExactlyOne_WhenLaunchesRace()
    {
        var service = NewService();
        using var start = new ManualResetEventSlim();
        var launches = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            start.Wait(TestContext.Current.CancellationToken);
            try { return service.postWork("review", "crew:review", Gated, exclusive: true); }
            catch (ExclusiveLaunchRefusedException) { return null; }
        }, TestContext.Current.CancellationToken)).ToArray();

        start.Set();
        var tickets = await Task.WhenAll(launches);

        var admitted = Assert.Single(tickets, t => t is not null);
        _gate.SetResult();
        await WaitTerminal(service.Registry.Get(admitted!)!);
    }

    [Fact]
    [Trait("BR", "#239")]
    public async Task ShouldAdmitTheExclusiveRelaunch_WhenTheCompletedOfTheHolderRelaunchesIt()
    {
        var service = NewService();
        var cmd = ChainHarness.Load(service, _tempDir, _crewsDir, "exclusive-async.cmd.ts", "exclusive-async");
        var console = new ScriptedTestConsole();

        await cmd.ExecuteAsync(ChainHarness.Ctx("exclusive-async", "", console), CancellationToken.None);
        await ChainHarness.WaitUntil(() => console.Output.Contains("exclusive-done:", StringComparison.Ordinal) || console.Output.Contains("refused:", StringComparison.Ordinal));

        Assert.DoesNotContain("refused:", console.Output, StringComparison.Ordinal);
        Assert.Contains("relaunched:t2", console.Output, StringComparison.Ordinal);
        Assert.Contains("exclusive-done:one-out", console.Output, StringComparison.Ordinal);
        Assert.Equal(2, service.Registry.List(CommandInstanceFilter.All).Count);
    }

    [Fact]
    [Trait("BR", "R-48")]
    public async Task ShouldRefuseExclusiveRelaunch_WhenCancelledWorkHasNotEnded()
    {
        var service = NewService();
        var first = service.postWork("review", "crew:review", Stubborn, exclusive: true);

        Assert.True(service.cancel(first));
        var refused = Assert.Throws<ExclusiveLaunchRefusedException>(() => service.postWork("review", "crew:review", Instant, exclusive: true));

        Assert.Equal(first, refused.Ticket);
        _stubbornGate.SetResult();
        await Task.CompletedTask;
    }

    [Fact]
    [Trait("BR", "R-48")]
    public async Task ShouldAcceptExclusiveRelaunch_WhenCancelledWorkHasEnded()
    {
        var service = NewService();
        var first = service.postWork("review", "crew:review", Stubborn, exclusive: true);
        Assert.True(service.cancel(first));
        _stubbornGate.SetResult();

        string? second = null;
        await Orkeon.Tests.Shared.Timing.Polling.WaitUntilAsync(() =>
        {
            try { second = service.postWork("review", "crew:review", Instant, exclusive: true); }
            catch (ExclusiveLaunchRefusedException) { /* the cancelled work has not ended yet */ }
            return second is not null;
        }, Orkeon.Tests.Shared.Timing.Polling.DefaultTimeout);

        Assert.NotNull(second);
        Assert.NotEqual(first, second);
        await WaitTerminal(service.Registry.Get(second)!);
    }

    [Fact]
    [Trait("BR", "R-49")]
    public async Task ShouldRefuseRunCrew_WhenSameCrewRunsExclusively()
    {
        var service = NewService();
        var facade = NewFacade(service);
        var first = service.postWork("one", "crew:one", Gated, exclusive: true);

        var refused = Assert.Throws<ExclusiveLaunchRefusedException>(() => facade.runCrew("one"));

        Assert.Equal(first, refused.Ticket);
        _gate.SetResult();
        await WaitTerminal(service.Registry.Get(first)!);
    }

    [Fact]
    [Trait("BR", "R-73")]
    public async Task ShouldRefuseLaunch_WhenExclusiveIsNull()
    {
        var service = NewService();
        var facade = NewFacade(service);

        var failure = Assert.Throws<Jint.Runtime.JavaScriptException>(() => facade.runCrewAsync("one", null, Options("({ exclusive: null })")));

        Assert.Equal("script-host.runCrewAsync: exclusive must be a boolean, not 'null'.", failure.Message);
        Assert.Empty(service.Registry.List(CommandInstanceFilter.All));
        var accepted = facade.runCrewAsync("one", null, Options("({})"));
        await WaitTerminal(service.Registry.Get(accepted)!);
    }
}
