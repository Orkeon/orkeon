using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Abstractions.Runners;
using Orkeon.Cli.Commands;
using Orkeon.Cli.Registry;
using Orkeon.Cli.Commands.Scripting.Loading;
using Orkeon.Cli.Commands.Scripting.Registry;
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
