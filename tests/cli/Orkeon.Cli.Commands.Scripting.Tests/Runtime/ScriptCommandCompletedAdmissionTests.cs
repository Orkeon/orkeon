using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Cli.Abstractions.Commands;
using Orkeon.Cli.Commands.Scripting.Dispatch;
using Orkeon.Cli.Commands.Scripting.Loading;
using Orkeon.Cli.Commands.Scripting.Registry;
using Orkeon.Cli.Commands.Scripting.Runtime;
using Orkeon.Cli.Commands.Scripting.Tests.Dispatch;
using Orkeon.Cli.Commands.Scripting.Tests.Fixtures;
using Orkeon.Infrastructure.Communication;
using Orkeon.Scripting;
using Orkeon.Scripting.Builders;
using Orkeon.Scripting.Toolchain;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Cli.Commands.Scripting.Tests.Runtime;

/// <summary>
/// R9: an instance launched by a <c>completed</c> handler joins the admission count of the
/// invocation that launched its parent; the slot is held until the last chained completion drains.
/// </summary>
[Collection("PromptScope")]
public sealed class ScriptCommandCompletedAdmissionTests : IDisposable
{
    private const string Quota = "Rejected: quota of 1 instance(s) of 'slot-chain' reached.";

    private readonly string _tempDir;
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _gates = new();
    private readonly CommandDispatchService _service;
    private readonly IInteractiveCommand _cmd;
    private readonly ScriptedTestConsole _console = new();
    private readonly List<string> _extraDirs = new();

    public ScriptCommandCompletedAdmissionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "orkeon-slot-chain-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _service = new CommandDispatchService(
            new InMemoryAgentChannel(NullLogger<InMemoryAgentChannel>.Instance),
            new AgentCommandDirectory(),
            new CommandInstanceRegistry(),
            maxChainDepth: 4);

        const string fixture = "slot-chain.cmd.ts";
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture), Path.Combine(_tempDir, fixture));
        var host = new ParkedHost(_service, Gate);
        var whitelist = new ScriptServiceWhitelist().AddOptional("script-host", _ => host).Build();
        var loader = new ScriptCommandLoader(
            new DiskBackedFileSystemService(_tempDir, virtualRoot: "/cmd"),
            PassThroughTranspiler.Instance,
            new JsEngineFactory(),
            Microsoft.Extensions.Options.Options.Create(new ScriptCommandLoaderOptions
            {
                Directories = ImmutableArray.Create("/cmd"),
                EsbuildTranspile = false,
            }),
            new ScriptCommandLoaderDependencies
            {
                LoggerFactory = null,
                Services = new ScriptServiceLocator(whitelist, ChainHarness.EmptyProvider.Instance),
                Dispatch = _service,
            });
        var (registry, _) = loader.LoadAndRegisterAsync(CancellationToken.None).GetAwaiter().GetResult();
        _cmd = registry.ScriptCommands.Single(c => c.Name == "slot-chain");
    }

    public void Dispose()
    {
        foreach (var g in _gates.Values) g.TrySetResult();
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
        foreach (var d in _extraDirs)
        {
            try { Directory.Delete(d, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    [Trait("BR", "R9")]
    public async Task ShouldHoldAdmissionSlot_WhenCompletedLaunchesAnInstance()
    {
        await LaunchAsync("slot-chain a");
        await ReleaseAndAwaitChainedAsync("first");

        var second = await _cmd.ExecuteAsync(ChainHarness.Ctx("slot-chain b", "b", _console), CancellationToken.None);

        Assert.Equal(Quota, second.Message);
        Assert.True(second.Failed);
    }

    [Fact]
    [Trait("BR", "R9")]
    public async Task ShouldRejectNewCommand_WhileItsChainHoldsTheSlot()
    {
        await LaunchAsync("slot-chain a");
        await ReleaseAndAwaitChainedAsync("first");

        var second = await _cmd.ExecuteAsync(ChainHarness.Ctx("slot-chain b", "b", _console), CancellationToken.None);

        Assert.Equal(Quota, second.Message);
        Assert.DoesNotContain("chain-done", _console.Output, StringComparison.Ordinal);
        Assert.Single(_service.Registry.List(new CommandInstanceFilter(State: CommandInstanceState.Running)));
    }

    [Fact]
    [Trait("BR", "R9")]
    public async Task ShouldReleaseSlot_WhenLastChainedCompletedIsDrained()
    {
        await LaunchAsync("slot-chain a");
        await ReleaseAndAwaitChainedAsync("first");
        var held = await _cmd.ExecuteAsync(ChainHarness.Ctx("slot-chain b", "b", _console), CancellationToken.None);
        Assert.Equal(Quota, held.Message);

        Gate("chain").TrySetResult();
        await ChainHarness.WaitUntil(() => _console.Output.Contains("chain-done", StringComparison.Ordinal));

        await AssertAcceptedEventuallyAsync();
    }

    [Fact]
    [Trait("BR", "R9")]
    public async Task ShouldKeepSlot_WhenFirstOfTwoInstancesLaunchesAChain()
    {
        await LaunchAsync("slot-chain two");
        await ReleaseAndAwaitChainedAsync("first");
        Gate("second").TrySetResult();
        await ChainHarness.WaitUntil(() => _service.Registry.List(CommandInstanceFilter.All)
            .Count(i => i.state == "done") >= 2);

        var second = await _cmd.ExecuteAsync(ChainHarness.Ctx("slot-chain b", "b", _console), CancellationToken.None);
        Assert.Equal(Quota, second.Message);

        Gate("chain").TrySetResult();
        await ChainHarness.WaitUntil(() => _console.Output.Contains("chain-done", StringComparison.Ordinal));
        await AssertAcceptedEventuallyAsync();
    }

    [Fact]
    [Trait("BR", "R9")]
    public async Task ShouldReleaseSlot_WhenAnotherCommandOfTheSameFileDrainsItsCompleted()
    {
        Gate("a-run").TrySetResult();
        var all = LoadAll("pair.cmd.ts");
        var a = all.Single(c => c.Name == "pair-a");
        var b = all.Single(c => c.Name == "pair-b");
        var first = await b.ExecuteAsync(ChainHarness.Ctx("pair-b x", "x", _console), CancellationToken.None);
        Assert.NotEqual(PairQuota, first.Message);

        // A1 parks its dispatch holding the shared engine lock; A2 queues on the lock first.
        var a1 = Task.Run(() => a.ExecuteAsync(ChainHarness.Ctx("pair-a park", "p", _console), CancellationToken.None));
        await ChainHarness.WaitUntil(() => Gate("park-entered").Task.IsCompleted);
        var a2 = a.ExecuteAsync(ChainHarness.Ctx("pair-a two", "t", _console), CancellationToken.None);

        Gate("b-run").TrySetResult();
        await ChainHarness.WaitUntil(() => _console.Output.Contains("b-run", StringComparison.Ordinal));
        Gate("park").TrySetResult();
        await a1;
        await a2;
        await ChainHarness.WaitUntil(() => _console.Output.Contains("b-completed", StringComparison.Ordinal));

        CommandResult? result = null;
        for (var i = 0; i < 100; i++)
        {
            result = await b.ExecuteAsync(ChainHarness.Ctx("pair-b z", "z", _console), CancellationToken.None);
            if (result.Message != PairQuota) break;
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
        Assert.NotEqual(PairQuota, result!.Message);
    }

    [Fact]
    [Trait("BR", "R9")]
    public async Task ShouldReleaseSlot_WhenCompletedThrowsAnEngineLimitException()
    {
        const string quota = "Rejected: quota of 1 instance(s) of 'limit-throw' reached.";
        var cmd = LoadAll("limit-throw.cmd.ts").Single(c => c.Name == "limit-throw");
        var first = await cmd.ExecuteAsync(ChainHarness.Ctx("limit-throw boom", "b", _console), CancellationToken.None);
        Assert.NotEqual(quota, first.Message);
        Gate("first").TrySetResult();
        await ChainHarness.WaitUntil(() => _console.Output.Contains("first", StringComparison.Ordinal));

        CommandResult? result = null;
        for (var i = 0; i < 100; i++)
        {
            result = await cmd.ExecuteAsync(ChainHarness.Ctx("limit-throw ok", "z", _console), CancellationToken.None);
            if (result.Message != quota) break;
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
        Assert.NotEqual(quota, result!.Message);
    }

    private const string PairQuota = "Rejected: quota of 1 instance(s) of 'pair-b' reached.";

    private IReadOnlyList<IInteractiveCommand> LoadAll(string fixture)
    {
        var dir = Path.Combine(Path.GetTempPath(), "orkeon-slot-extra-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _extraDirs.Add(dir);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture), Path.Combine(dir, fixture));
        var service = new CommandDispatchService(
            new InMemoryAgentChannel(NullLogger<InMemoryAgentChannel>.Instance),
            new AgentCommandDirectory(),
            new CommandInstanceRegistry(),
            maxChainDepth: 4);
        var host = new ParkedHost(service, Gate);
        var whitelist = new ScriptServiceWhitelist().AddOptional("script-host", _ => host).Build();
        var loader = new ScriptCommandLoader(
            new DiskBackedFileSystemService(dir, virtualRoot: "/cmd"),
            PassThroughTranspiler.Instance,
            new JsEngineFactory(),
            Microsoft.Extensions.Options.Options.Create(new ScriptCommandLoaderOptions
            {
                Directories = ImmutableArray.Create("/cmd"),
                EsbuildTranspile = false,
            }),
            new ScriptCommandLoaderDependencies
            {
                LoggerFactory = null,
                Services = new ScriptServiceLocator(whitelist, ChainHarness.EmptyProvider.Instance),
                Dispatch = service,
            });
        var (registry, _) = loader.LoadAndRegisterAsync(CancellationToken.None).GetAwaiter().GetResult();
        return registry.ScriptCommands.ToList();
    }

    private async Task LaunchAsync(string raw)
    {
        var first = await _cmd.ExecuteAsync(ChainHarness.Ctx(raw, "a", _console), CancellationToken.None);
        Assert.NotEqual(Quota, first.Message);
    }

    private async Task ReleaseAndAwaitChainedAsync(string label)
    {
        Gate(label).TrySetResult();
        await ChainHarness.WaitUntil(() => _console.Output.Contains("chained:", StringComparison.Ordinal));
        Assert.Contains("chained:", _console.Output, StringComparison.Ordinal);
    }

    private async Task AssertAcceptedEventuallyAsync()
    {
        CommandResult? result = null;
        for (var i = 0; i < 100; i++)
        {
            result = await _cmd.ExecuteAsync(ChainHarness.Ctx("slot-chain z", "z", _console), CancellationToken.None);
            if (result.Message != Quota) break;
            await Task.Delay(20);
        }
        Assert.NotEqual(Quota, result!.Message);
    }

    private TaskCompletionSource Gate(string label)
        => _gates.GetOrAdd(label, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

    /// <summary>Stand-in for <c>script-host</c>: each run parks until the test opens its gate.</summary>
    private sealed class ParkedHost
    {
        private readonly CommandDispatchService _service;
        private readonly Func<string, TaskCompletionSource> _gate;

        public ParkedHost(CommandDispatchService service, Func<string, TaskCompletionSource> gate)
        {
            _service = service;
            _gate = gate;
        }

        public string park()
        {
            _gate("park-entered").TrySetResult();
            _gate("park").Task.Wait();
            return "ok";
        }

        public string runCrewAsync(string name)
            => _service.postWork(name, "crew:" + name, async ct =>
            {
                await _gate(name).Task.WaitAsync(ct).ConfigureAwait(false);
                return new CommandResponse(name, name, true, name + "-out", null);
            });
    }
}
