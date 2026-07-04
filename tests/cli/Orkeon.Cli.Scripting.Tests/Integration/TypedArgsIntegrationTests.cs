using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Abstractions.Runners;
using Orkeon.Cli.Commands;
using Orkeon.Cli.Registry;
using Orkeon.Cli.Scripting.Loading;
using Orkeon.Cli.Scripting.Registry;
using Orkeon.Cli.Scripting.Tests.Fixtures;
using Orkeon.Scripting;
using Orkeon.Scripting.Toolchain;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Cli.Scripting.Tests.Integration;

/// <summary>
/// Phase 3 end-to-end: <c>deploy --target=preprod --crew=alpha --dry</c> drives a typed
/// args handler through the real <see cref="InteractiveRunnerBase"/>, and
/// <c>help-cmd deploy</c> dumps the formatted signature.
/// </summary>
public sealed class TypedArgsIntegrationTests : IDisposable
{
    private readonly string _tempDir;

    public TypedArgsIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "orkeon-cli-scripting-it3-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "typed-deploy.cmd.ts"),
            Path.Combine(_tempDir, "deploy.cmd.ts"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    private async Task<(ScriptCommandRegistry Registry, DefaultCommandRegistry Defaults, IServiceProvider Services)> BuildAsync()
    {
        var fs = new DiskBackedFileSystemService(_tempDir, virtualRoot: "/cmd");
        var loader = new ScriptCommandLoader(
            fs, PassThroughTranspiler.Instance, new JsEngineFactory(),
            new ScriptCommandLoaderOptions { Directories = ImmutableArray.Create("/cmd") });
        var (registry, _) = await loader.LoadAndRegisterAsync(CancellationToken.None);
        var defaults = new DefaultCommandRegistry(new HelpCommand(), new ExitCommand(), new ClearCommand());
        var services = new ServiceCollection().AddSingleton(defaults).BuildServiceProvider();
        return (registry, defaults, services);
    }

    [Fact]
    public async Task Typed_args_reach_handler()
    {
        var (registry, defaults, services) = await BuildAsync();
        var console = new ScriptedTestConsole();
        var runner = new TestRunner(defaults, registry, console, services);

        console.EnqueueLine("deploy --target=preprod --crew=alpha --dry");
        console.EnqueueLine("exit");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await runner.RunAsync(cts.Token);

        Assert.Contains("deploy alpha -> preprod [DRY]", console.Output);
    }

    [Fact]
    public async Task Invalid_choice_yields_error_message_and_skips_handler()
    {
        var (registry, defaults, services) = await BuildAsync();
        var console = new ScriptedTestConsole();
        var runner = new TestRunner(defaults, registry, console, services);

        console.EnqueueLine("deploy --target=staging --crew=alpha");
        console.EnqueueLine("exit");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await runner.RunAsync(cts.Token);

        Assert.Contains("Error:", console.Output);
        Assert.Contains("choices", console.Output);
        Assert.DoesNotContain("deploy alpha", console.Output);  // handler should not have run
    }

    [Fact]
    public async Task Missing_required_yields_error()
    {
        var (registry, defaults, services) = await BuildAsync();
        var console = new ScriptedTestConsole();
        var runner = new TestRunner(defaults, registry, console, services);

        console.EnqueueLine("deploy --target=dev");
        console.EnqueueLine("exit");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await runner.RunAsync(cts.Token);

        Assert.Contains("--crew", console.Output);
    }

    [Fact]
    public async Task Help_cmd_renders_args_signature()
    {
        var (registry, defaults, services) = await BuildAsync();
        var console = new ScriptedTestConsole();
        var runner = new TestRunner(defaults, registry, console, services);

        console.EnqueueLine("help-cmd deploy");
        console.EnqueueLine("exit");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await runner.RunAsync(cts.Token);

        var output = console.Output;
        Assert.Contains("deploy", output);
        Assert.Contains("Deploy a crew to a target", output);
        Assert.Contains("--target", output);
        Assert.Contains("--crew", output);
        Assert.Contains("--dry", output);
        Assert.Contains("choices: dev | preprod | prod", output);
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
        protected override string Banner => "(phase3 banner)";
        protected override string Prompt => "p3> ";
    }
}
