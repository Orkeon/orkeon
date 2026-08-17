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
/// End-to-end smoke test against the actual <c>examples/cli-ts-commands/hello.cmd.ts</c>
/// script — proves the demo path documented in <c>examples/cli-ts-commands/README.md</c>
/// works without requiring a subprocess launch.
/// </summary>
public sealed class HelloDemoSmokeTests : IDisposable
{
    private readonly string _tempDir;

    public HelloDemoSmokeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "orkeon-cli-scripting-demo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        // Find the repo-root examples folder. The test runs from bin/<config>/<tfm>; walk
        // up until we find the marker.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Orkeon.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var src = Path.Combine(dir!.FullName, "examples", "cli-ts-commands", "hello.cmd.ts");
        Assert.True(File.Exists(src), $"Demo script not found at {src}");
        File.Copy(src, Path.Combine(_tempDir, "hello.cmd.ts"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task Demo_hello_command_runs_with_default_and_with_arg()
    {
        var fs = new DiskBackedFileSystemService(_tempDir, virtualRoot: "/cmd");
        var loader = new ScriptCommandLoader(
            fs, PassThroughTranspiler.Instance, new JsEngineFactory(),
            new ScriptCommandLoaderOptions { Directories = ImmutableArray.Create("/cmd") });
        var (registry, summary) = await loader.LoadAndRegisterAsync(CancellationToken.None);
        Assert.Equal(1, registry.Count);
        Assert.False(summary.HasIssues);

        var defaults = new DefaultCommandRegistry(new HelpCommand(), new ExitCommand(), new ClearCommand());
        var console = new ScriptedTestConsole();
        var services = new ServiceCollection().AddSingleton(defaults).BuildServiceProvider();
        var runner = new TestRunner(defaults, registry, console, services);

        console.EnqueueLine("hello");                  // default 'world'
        console.EnqueueLine("hello --who=Cyril");      // typed arg
        console.EnqueueLine("help-cmd hello");         // formatted signature
        console.EnqueueLine("exit");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await runner.RunAsync(cts.Token);

        var output = console.Output;
        Assert.Contains("Hello, world!", output);
        Assert.Contains("Hello, Cyril!", output);
        Assert.Contains("--who", output);
        Assert.Contains("Greet someone", output);
    }

    private sealed class TestRunner : InteractiveRunnerBase
    {
        public TestRunner(DefaultCommandRegistry d, ScriptCommandRegistry s, IConsoleAdapter c, IServiceProvider sp)
            : base(d, s, c, NullLogger<TestRunner>.Instance, sp) { }
        protected override string Banner => "(demo smoke)";
        protected override string Prompt => "demo> ";
    }
}
