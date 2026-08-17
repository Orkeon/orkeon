using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Cli.Abstractions.Commands;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Abstractions.Runners;
using Orkeon.Cli.Commands;
using Orkeon.Cli.Registry;
using Orkeon.Cli.Commands.Scripting.Dispatch;
using Orkeon.Cli.Commands.Scripting.Loading;
using Orkeon.Cli.Commands.Scripting.Registry;
using Orkeon.Cli.Commands.Scripting.Runtime;
using Orkeon.Cli.Commands.Scripting.Tests.Fixtures;
using Orkeon.Domain.Common;
using Orkeon.Infrastructure.Communication;
using Orkeon.Scripting;
using Orkeon.Scripting.Builders;
using Orkeon.Scripting.Toolchain;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Cli.Commands.Scripting.Tests.Dispatch;

/// <summary>
/// End-to-end coverage of the command-dispatch layer (design §8): a <c>.cmd.ts</c> command
/// dispatches by name to an agent that declared <c>onCommand</c>, exercising the sync path,
/// the async post + completed-drain path, and the per-command admission quota.
/// </summary>
public sealed class CommandDispatchIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly List<IDisposable> _disposables = new();

    public CommandDispatchIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "orkeon-dispatch-it-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        foreach (var d in _disposables) { try { d.Dispose(); } catch { /* best effort */ } }
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task Sync_request_round_trips_through_agent_onCommand()
    {
        var service = NewDispatchService();
        RegisterEchoAgent(service); // onCommand uppercases the payload

        var registry = LoadFixture("ask.cmd.ts", service);
        var console = new ScriptedTestConsole();
        var runner = NewRunner(registry, console);

        console.EnqueueLine("ask hello");
        console.EnqueueLine("exit");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await runner.RunAsync(cts.Token);

        Assert.Contains("reply:HELLO", console.Output);
    }

    [Fact]
    public async Task Async_post_completes_and_completed_drains_on_next_pump()
    {
        var service = NewDispatchService();
        RegisterEchoAgent(service);

        var registry = LoadFixture("askbg.cmd.ts", service);
        var askbg = registry.ScriptCommands.Single(c => c.Name == "askbg");
        var console = new ScriptedTestConsole();

        // 1st invocation: dispatch detaches; the prompt returns immediately.
        await askbg.ExecuteAsync(Ctx("askbg hello", "hello", console), CancellationToken.None);

        // The background request settles; wait for the instance to reach a terminal state.
        await WaitUntil(() => service.Registry.List(new CommandInstanceFilter(State: CommandInstanceState.Done)).Count >= 1);

        // Next invocations on the same engine are the pumps that replay completed() for the
        // 1st. The drain enqueue (terminal callback) happens just AFTER the state flips to
        // Done, so a single pump can race it — pump until the replay is observed.
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!console.Output.Contains("done:HELLO", StringComparison.Ordinal))
        {
            if (DateTime.UtcNow > deadline) break;
            await askbg.ExecuteAsync(Ctx("askbg world", "world", console), CancellationToken.None);
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.Contains("done:HELLO", console.Output);
    }

    [Fact]
    public async Task Async_dispatch_with_a_pending_promise_still_launches_before_the_prompt_returns()
    {
        // The /assistant shape since B-5: `async dispatch` suspends on a Task-backed await
        // BEFORE posting. Pre-fix, ExecuteAsync returned with the body still pending and the
        // launch only happened at the NEXT engine pump — a user typing one free-text line
        // saw nothing happen, ever.
        var service = NewDispatchService();
        RegisterEchoAgent(service);

        var registry = LoadFixture("askbg-async.cmd.ts", service, w => w.AddOptional("slow", _ => new SlowService()));
        var cmd = registry.ScriptCommands.Single(c => c.Name == "askbg-async");
        var console = new ScriptedTestConsole();

        await cmd.ExecuteAsync(Ctx("askbg-async hello", "hello", console), CancellationToken.None);

        // The launch completed during ExecuteAsync: the instance is already registered.
        Assert.Single(service.Registry.List());

        // And the completion push-drains without any further command invocation.
        await WaitUntil(() => console.Output.Contains("done:HELLO", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Async_admission_quota_rejects_a_second_in_flight_instance()
    {
        var service = NewDispatchService();

        // A deliberately slow agent named "echo" (the fixture's target) so the first instance
        // stays Running and keeps the maxConcurrent:1 slot held.
        var gate = new TaskCompletionSource();
        var slowId = AgentId.Create();
        service.Directory.Register("echo", slowId);
        _disposables.Add(service.Channel.RegisterHandler(slowId, async (req, ct) =>
        {
            await gate.Task.ConfigureAwait(false);
            return AgentChannelResponse.Ok(req.CorrelationId, slowId, "eventually");
        }));

        var registry = LoadFixture("askbg.cmd.ts", service);
        var askbg = registry.ScriptCommands.Single(c => c.Name == "askbg");
        var console = new ScriptedTestConsole();

        // 1st: acquires the lone slot and stays in flight.
        await askbg.ExecuteAsync(Ctx("askbg a", "a", console), CancellationToken.None);
        // 2nd: slot is held → admission refuses, dispatch never runs.
        var rejected = await askbg.ExecuteAsync(Ctx("askbg b", "b", console), CancellationToken.None);

        Assert.Contains("quota of 1", rejected.Message);
        Assert.Single(service.Registry.List(new CommandInstanceFilter(State: CommandInstanceState.Running)));

        gate.SetResult(); // let the first finish so background threads unwind cleanly
    }

    [Fact]
    public async Task Builtin_ps_inspect_and_result_report_in_flight_instances()
    {
        var service = NewDispatchService();

        // Slow agent keeps the instance Running so ps/inspect have something to show.
        var gate = new TaskCompletionSource();
        var id = AgentId.Create();
        service.Directory.Register("echo", id);
        _disposables.Add(service.Channel.RegisterHandler(id, async (req, ct) =>
        {
            await gate.Task.ConfigureAwait(false);
            return AgentChannelResponse.Ok(req.CorrelationId, id, "later");
        }));

        var registry = LoadFixture("askbg.cmd.ts", service);
        var console = new ScriptedTestConsole();

        await registry.ScriptCommands.Single(c => c.Name == "askbg")
            .ExecuteAsync(Ctx("askbg a", "a", console), CancellationToken.None);

        var ticket = service.Registry.List(new CommandInstanceFilter(State: CommandInstanceState.Running)).Single().ticket;

        // ps (default state=running) lists the in-flight instance.
        await Builtin(registry, "ps").ExecuteAsync(Ctx("ps", "", console), CancellationToken.None);
        Assert.Contains("echo", console.Output);

        // inspect shows the detail by ticket.
        await Builtin(registry, "inspect").ExecuteAsync(Ctx($"inspect --ticket={ticket}", $"--ticket={ticket}", console), CancellationToken.None);
        Assert.Contains(ticket, console.Output);

        // result reports it as still running.
        var result = await Builtin(registry, "result").ExecuteAsync(Ctx($"result --ticket={ticket}", $"--ticket={ticket}", console), CancellationToken.None);
        Assert.Contains("still running", result.Message);

        gate.SetResult();
    }

    private static IInteractiveCommand Builtin(ScriptCommandRegistry registry, string name)
        => registry.Commands.Single(c => string.Equals(c.Name, name, StringComparison.Ordinal));

    // ---- helpers --------------------------------------------------------------------------

    private static CommandDispatchService NewDispatchService()
        => new(new InMemoryAgentChannel(NullLogger<InMemoryAgentChannel>.Instance),
               new AgentCommandDirectory(),
               new CommandInstanceRegistry());

    private void RegisterEchoAgent(CommandDispatchService service)
    {
        var engine = new JsEngineFactory().Create();
        var agent = (JsAgent)engine.Evaluate(
            "agentBuilder().name('echo').role('r').goal('g')" +
            ".onCommand(function(env){ return env.payload.toUpperCase(); }).build();").ToObject()!;
        var gate = new SemaphoreSlim(1, 1);
        _disposables.Add(gate);
        _disposables.Add(AgentCommandRegistrar.Register(agent, engine, gate, service.Channel, service.Directory));
    }

    private ScriptCommandRegistry LoadFixture(
        string fixture, CommandDispatchService service, Action<ScriptServiceWhitelist>? extraServices = null)
    {
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture), Path.Combine(_tempDir, fixture));

        var builder = new ScriptServiceWhitelist().AddOptional("commands", _ => service);
        extraServices?.Invoke(builder);
        var whitelist = builder.Build();
        var locator = new ScriptServiceLocator(whitelist, EmptyProvider.Instance);

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
                Services = locator,
                Dispatch = service,
            });

        var (registry, _) = loader.LoadAndRegisterAsync(CancellationToken.None).GetAwaiter().GetResult();
        return registry;
    }

    private static CommandContext Ctx(string raw, string arg, IConsoleAdapter console) => new()
    {
        RawInput = raw,
        Args = new[] { arg },
        Console = console,
        Scope = EmptyProvider.Instance,
    };

    private static async Task WaitUntil(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!predicate())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("condition not met in time");
            await Task.Delay(20);
        }
    }

    private static TestRunner NewRunner(ScriptCommandRegistry registry, ScriptedTestConsole console)
    {
        var defaults = new DefaultCommandRegistry(new HelpCommand(), new ExitCommand(), new ClearCommand());
        var services = new ServiceCollection().AddSingleton(defaults).BuildServiceProvider();
        return new TestRunner(defaults, registry, console, services);
    }

    private sealed class TestRunner(
        DefaultCommandRegistry defaults,
        ScriptCommandRegistry specific,
        IConsoleAdapter console,
        IServiceProvider services)
        : InteractiveRunnerBase(defaults, specific, console, NullLogger<TestRunner>.Instance, services)
    {
        protected override string Banner => "(dispatch test)";
        protected override string Prompt => "test> ";
    }

    private sealed class EmptyProvider : IServiceProvider
    {
        public static readonly EmptyProvider Instance = new();
        public object? GetService(Type serviceType) => null;
    }

    /// <summary>A service whose method returns a genuinely pending Task (never synchronously complete).</summary>
    /// <remarks>Instance member on purpose: Jint's interop exposes instance methods on the wrapped object.</remarks>
    private sealed class SlowService
    {
        private readonly TimeSpan _delay = TimeSpan.FromMilliseconds(100);

        public async Task<string> WaitAsync()
        {
            await Task.Delay(_delay).ConfigureAwait(false);
            return "ok";
        }
    }
}
