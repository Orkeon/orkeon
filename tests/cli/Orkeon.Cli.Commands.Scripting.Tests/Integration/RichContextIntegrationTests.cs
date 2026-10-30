using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Cli.Abstractions.Commands;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Abstractions.Runners;
using Orkeon.Cli.Commands;
using Orkeon.Cli.Registry;
using Orkeon.Cli.Commands.Scripting.Loading;
using Orkeon.Cli.Commands.Scripting.Registry;
using Orkeon.Cli.Commands.Scripting.Runtime;
using Orkeon.Cli.Commands.Scripting.Tests.Fixtures;
using Orkeon.Scripting;
using Orkeon.Scripting.Toolchain;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Cli.Commands.Scripting.Tests.Integration;

/// <summary>
/// Phase 2 integration: a scripted command exercises <c>ctx.log</c>, <c>ctx.write</c>,
/// and <c>ctx.prompt(confirm)</c> through a real <see cref="InteractiveRunnerBase"/>.
/// Plus a Ctrl+C-style cancellation flow.
/// </summary>
public sealed class RichContextIntegrationTests : IDisposable
{
    private readonly string _tempDir;

    public RichContextIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "orkeon-cli-scripting-it2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    private void CopyFixture(string name)
        => File.Copy(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", name),
            Path.Combine(_tempDir, name));

    private async Task<ScriptCommandRegistry> LoadRegistryAsync()
    {
        var fs = new DiskBackedFileSystemService(_tempDir, virtualRoot: "/cmd");
        var loader = new ScriptCommandLoader(
            fs, PassThroughTranspiler.Instance, new JsEngineFactory(),
            new ScriptCommandLoaderOptions { Directories = ImmutableArray.Create("/cmd") });
        var (registry, _) = await loader.LoadAndRegisterAsync(CancellationToken.None);
        return registry;
    }

    [Fact]
    public async Task Prompt_handler_receives_confirm_answer_and_continues()
    {
        CopyFixture("prompt-script.cmd.ts");
        var scriptRegistry = await LoadRegistryAsync();
        var defaults = new DefaultCommandRegistry(new HelpCommand(), new ExitCommand(), new ClearCommand());
        var console = new ScriptedTestConsole();
        var services = new ServiceCollection().AddSingleton(defaults).BuildServiceProvider();
        var runner = new TestRunner(defaults, scriptRegistry, console, services);

        console.EnqueueLine("deploy");
        console.EnqueueLine("y");      // answer to ctx.prompt(confirm)
        console.EnqueueLine("exit");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await runner.RunAsync(cts.Token);

        var output = console.Output;
        Assert.Contains("about to deploy", output);
        Assert.Contains("Proceed?", output);
        Assert.Contains("ok", output);  // continue("ok") message rendered by the runner
    }

    [Fact]
    public async Task Cancelling_command_makes_signal_observable_in_script()
    {
        CopyFixture("cancellation-script.cmd.ts");
        var scriptRegistry = await LoadRegistryAsync();
        var defaults = new DefaultCommandRegistry(new HelpCommand(), new ExitCommand(), new ClearCommand());
        var console = new ScriptedTestConsole();
        var services = new ServiceCollection().AddSingleton(defaults).BuildServiceProvider();
        var runner = new TestRunner(defaults, scriptRegistry, console, services);

        console.EnqueueLine("wait");

        // Run the loop on a background task so we can signal cancellation while the
        // handler busy-waits inside engine.Invoke.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var runTask = Task.Run(() => runner.RunAsync(cts.Token), TestContext.Current.CancellationToken);

        // Wait until the handler is actually executing inside the engine before cancelling.
        // IsCommandRunning is NOT enough: it flips true before ExecuteAsync's preamble
        // (engine-lock WaitAsync on the per-command token), so cancelling on that signal
        // can abort the command host-side ("Command 'wait' cancelled.") before the script
        // ever observes ctx.signal — the fixture prints "wait-started" as its first
        // statement precisely so we cancel only once the graceful path is guaranteed.
        var waitStart = DateTime.UtcNow;
        while (!console.Output.Contains("wait-started") && DateTime.UtcNow - waitStart < TimeSpan.FromSeconds(10))
            await Task.Delay(25, TestContext.Current.CancellationToken);
        Assert.Contains("wait-started", console.Output);

        runner.RequestCommandCancellation();

        // Wait for the handler to observe cancellation and the runner to print the result.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline && !console.Output.Contains("cancelled-from-script"))
            await Task.Delay(25, TestContext.Current.CancellationToken);

        // Tell the runner to exit.
        console.EnqueueLine("exit");
        await runTask;

        Assert.Contains("cancelled-from-script", console.Output);
    }

    [Fact]
    public async Task ShouldListThePendingApprovalWithItsTaskAndTool_ThenNothingOnceItIsAnswered()
    {
        CopyFixture("pending-approvals.cmd.ts");
        var ct = TestContext.Current.CancellationToken;
        using var channel = new ConsoleToolApprovalChannel(() => new ScriptedTestConsole(), () => ("t1", "dev-batches"));
        using var services = new ServiceCollection()
            .AddSingleton<IToolApprovalWatch>(channel)
            .BuildServiceProvider();
        var loader = new ScriptCommandLoader(
            new DiskBackedFileSystemService(_tempDir, virtualRoot: "/cmd"),
            PassThroughTranspiler.Instance,
            new JsEngineFactory(),
            Microsoft.Extensions.Options.Options.Create(new ScriptCommandLoaderOptions { Directories = ["/cmd"], EsbuildTranspile = false }),
            new ScriptCommandLoaderDependencies
            {
                Services = new ScriptServiceLocator(DefaultScriptServiceWhitelist.Build().Build(), services),
            });
        var (registry, _) = await loader.LoadAndRegisterAsync(ct);
        var command = registry.ScriptCommands.Single(c => c.Name == "pending");

        var decision = channel.RequestAsync(new ToolApprovalRequest
        {
            ToolName = "shell_command",
            Arguments = ImmutableDictionary.CreateRange([KeyValuePair.Create<string, object?>("command", "npm test")]),
            Mode = "default",
            Reason = "tool 'shell_command' requires approval (mode 'default').",
        }, ct);
        var waiting = new ScriptedTestConsole();
        await command.ExecuteAsync(new CommandContext { RawInput = "/pending", Args = [], Console = waiting, Scope = services }, ct);

        Assert.Contains("approvals:1", waiting.Output, StringComparison.Ordinal);
        Assert.Contains("[t1] dev-batches waits on shell_command {\"command\":\"npm test\"}", waiting.Output, StringComparison.Ordinal);
        Assert.Contains("\"taskId\":\"t1\"", waiting.Output, StringComparison.Ordinal);
        Assert.Contains("\"tool\":\"shell_command\"", waiting.Output, StringComparison.Ordinal);

        Assert.True(channel.TryConsume("y"));
        Assert.Equal(ToolApprovalDecision.AllowOnce, await decision);
        var answered = new ScriptedTestConsole();
        await command.ExecuteAsync(new CommandContext { RawInput = "/pending", Args = [], Console = answered, Scope = services }, ct);

        Assert.Contains("approvals:0", answered.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("waits on", answered.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldPrintTheRuntimeVersionAndChannel_WhenTheHostRegisteredThem()
    {
        CopyFixture("runtime-version.cmd.ts");
        var ct = TestContext.Current.CancellationToken;
        using var services = new ServiceCollection()
            .AddSingleton(ScriptRuntimeInfo.FromInformationalVersion("1.0.0-rc.6+3ac6aa64", "source"))
            .BuildServiceProvider();
        var loader = new ScriptCommandLoader(
            new DiskBackedFileSystemService(_tempDir, virtualRoot: "/cmd"),
            PassThroughTranspiler.Instance,
            new JsEngineFactory(),
            Microsoft.Extensions.Options.Options.Create(new ScriptCommandLoaderOptions { Directories = ["/cmd"], EsbuildTranspile = false }),
            new ScriptCommandLoaderDependencies
            {
                Services = new ScriptServiceLocator(DefaultScriptServiceWhitelist.Build().Build(), services),
            });
        var (registry, _) = await loader.LoadAndRegisterAsync(ct);
        var command = registry.ScriptCommands.Single(c => c.Name == "runtime-version");

        var console = new ScriptedTestConsole();
        await command.ExecuteAsync(new CommandContext { RawInput = "/runtime-version", Args = [], Console = console, Scope = services }, ct);

        Assert.Contains("version:1.0.0-rc.6+3ac6aa64", console.Output, StringComparison.Ordinal);
        Assert.Contains("channel:source", console.Output, StringComparison.Ordinal);
    }

    /// <summary>#226 - a command lists the session's usage by model and by command, with a cost where a price is set.</summary>
    [Trait("BR", "CH-226")]
    [Fact]
    public async Task ShouldListTheSessionUsageByModelAndByCommand_WithACostWhereAPriceIsSet()
    {
        CopyFixture("session-usage.cmd.ts");
        var ct = TestContext.Current.CancellationToken;
        var instances = new Orkeon.Cli.Commands.Scripting.Dispatch.CommandInstanceRegistry();
        var crew = instances.Register("dev-batches", Orkeon.Cli.Commands.Scripting.Dispatch.CommandInstanceKind.Async, "crew:dev", "dev", Guid.NewGuid());
        crew.AddUsage(new Orkeon.Application.Interfaces.Ports.CostUsageEvent { PromptTokens = 900, CompletionTokens = 100, CacheHitTokens = 600 }, 0.0125m);
        var cost = new Doubles.MockCostBudgetManager
        {
            Report = new Orkeon.Application.Interfaces.Ports.CostReport
            {
                TotalCost = 0.0125m,
                TotalCalls = 2,
                TotalUsage = new() { PromptTokens = 910, CompletionTokens = 105, CacheReadTokens = 600, ReasoningTokens = 40, Calls = 2, UnpricedCalls = 1, Cost = 0.0125m, Priced = true },
                ModelUsageByModel = new Dictionary<string, Orkeon.Application.Interfaces.Ports.ModelUsage>
                {
                    ["gpt-4o"] = new() { PromptTokens = 900, CompletionTokens = 100, CacheReadTokens = 600, ReasoningTokens = 40, Calls = 1, Cost = 0.0125m, Priced = true },
                    ["local-model"] = new() { PromptTokens = 10, CompletionTokens = 5, Calls = 1, UnpricedCalls = 1 },
                },
            },
        };
        using var services = new ServiceCollection()
            .AddSingleton<Orkeon.Application.Interfaces.Ports.ICostBudgetManager>(cost)
            .AddSingleton(instances)
            .BuildServiceProvider();
        var loader = new ScriptCommandLoader(
            new DiskBackedFileSystemService(_tempDir, virtualRoot: "/cmd"),
            PassThroughTranspiler.Instance,
            new JsEngineFactory(),
            Microsoft.Extensions.Options.Options.Create(new ScriptCommandLoaderOptions { Directories = ["/cmd"], EsbuildTranspile = false }),
            new ScriptCommandLoaderDependencies
            {
                Services = new ScriptServiceLocator(DefaultScriptServiceWhitelist.Build().Build(), services),
            });
        var (registry, _) = await loader.LoadAndRegisterAsync(ct);
        var command = registry.ScriptCommands.Single(c => c.Name == "usage");

        var console = new ScriptedTestConsole();
        await command.ExecuteAsync(new CommandContext { RawInput = "/usage", Args = [], Console = console, Scope = services }, ct);

        Assert.Contains("total 1015 tokens, 2 calls (1 unpriced), $0.0125", console.Output, StringComparison.Ordinal);
        Assert.Contains("model gpt-4o in=900 out=100 cache=600 reasoning=40 unpriced=0 $0.0125", console.Output, StringComparison.Ordinal);
        Assert.Contains("model local-model in=10 out=5 cache=n/a reasoning=n/a unpriced=1 n/a", console.Output, StringComparison.Ordinal);
        Assert.Contains($"command {crew.Ticket} dev-batches in=900 out=100 cache=600 calls=1 unpriced=0 $0.0125", console.Output, StringComparison.Ordinal);
    }

    private sealed class TestRunner : InteractiveRunnerBase
    {
        public TestRunner(
            DefaultCommandRegistry defaults,
            ScriptCommandRegistry specific,
            IConsoleAdapter console,
            IServiceProvider services)
            : base(defaults, specific, console, NullLogger<TestRunner>.Instance, services)
        {
        }

        protected override string Banner => "(phase2 banner)";
        protected override string Prompt => "p2> ";
    }
}
