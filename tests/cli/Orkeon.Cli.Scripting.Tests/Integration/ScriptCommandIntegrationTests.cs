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
/// End-to-end Phase 1 integration: a real <see cref="InteractiveRunnerBase"/> drives a
/// scripted command produced by <see cref="ScriptCommandLoader"/>.
/// </summary>
public sealed class ScriptCommandIntegrationTests : IDisposable
{
    private readonly string _tempDir;

    public ScriptCommandIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "orkeon-cli-scripting-it-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task Scripted_command_executes_through_real_runner()
    {
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "echo-args.cmd.ts"),
            Path.Combine(_tempDir, "echo.cmd.ts"));

        var fs = new DiskBackedFileSystemService(_tempDir, virtualRoot: "/cmd");
        var loader = new ScriptCommandLoader(
            fs, PassThroughTranspiler.Instance, new JsEngineFactory(),
            new ScriptCommandLoaderOptions { Directories = ImmutableArray.Create("/cmd") });

        var (scriptRegistry, _) = await loader.LoadAndRegisterAsync(CancellationToken.None);
        Assert.Equal(1, scriptRegistry.Count);

        // Default registry — help/exit/clear.
        var defaultRegistry = new DefaultCommandRegistry(
            new HelpCommand(), new ExitCommand(), new ClearCommand());

        var console = new ScriptedTestConsole();
        var services = new ServiceCollection()
            .AddSingleton(defaultRegistry)
            .BuildServiceProvider();

        var runner = new TestRunner(defaultRegistry, scriptRegistry, console, services);

        // Driver: send "echo a b c" then "exit" so the runner returns.
        console.EnqueueLine("echo a b c");
        console.EnqueueLine("exit");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await runner.RunAsync(cts.Token);

        Assert.Contains("echo:a,b,c", console.Output);
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

        protected override string Banner => "(test banner)";
        protected override string Prompt => "test> ";
    }
}
