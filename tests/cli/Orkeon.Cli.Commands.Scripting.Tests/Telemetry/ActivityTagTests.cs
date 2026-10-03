using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Abstractions.Runners;
using Orkeon.Cli.Commands;
using Orkeon.Cli.Registry;
using Orkeon.Cli.Commands.Scripting.Loading;
using Orkeon.Cli.Commands.Scripting.Registry;
using Orkeon.Cli.Commands.Scripting.Telemetry;
using Orkeon.Cli.Commands.Scripting.Tests.Fixtures;
using Orkeon.Scripting;
using Orkeon.Scripting.Telemetry;
using Orkeon.Scripting.Toolchain;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tests.Shared.Telemetry;

namespace Orkeon.Cli.Commands.Scripting.Tests.Telemetry;

/// <summary>
/// Verifies the spec §10 commitment: every scripted command invocation emits an
/// <see cref="Activity"/> tagged with the source script's virtual path.
/// </summary>
public sealed class ActivityTagTests : IDisposable
{
    private readonly string _tempDir;

    public ActivityTagTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "orkeon-cli-scripting-otel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "echo-args.cmd.ts"),
            Path.Combine(_tempDir, "echo.cmd.ts"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task Scripted_command_invocation_tags_source_script_path()
    {
        var fs = new DiskBackedFileSystemService(_tempDir, virtualRoot: "/cmd");
        var loader = new ScriptCommandLoader(
            fs, PassThroughTranspiler.Instance, new JsEngineFactory(),
            new ScriptCommandLoaderOptions { Directories = ImmutableArray.Create("/cmd") });
        var (registry, _) = await loader.LoadAndRegisterAsync(CancellationToken.None);

        var defaults = new DefaultCommandRegistry(new HelpCommand(), new ExitCommand(), new ClearCommand());
        var console = new ScriptedTestConsole();
        var services = new ServiceCollection().AddSingleton(defaults).BuildServiceProvider();
        var runner = new TestRunner(defaults, registry, console, services);

        console.EnqueueLine("echo hello");
        console.EnqueueLine("exit");

        // The listener hears the whole process, and other tests run this very script: the run
        // happens under a root of this test's own, and the assertions read a copy of its trace
        // (GAP-41).
        using var recorder = new ActivityRecorder(ScriptingActivitySource.Name);
        ActivityTraceId trace;
        using (var root = new Activity(nameof(Scripted_command_invocation_tags_source_script_path)))
        {
            root.Start();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await runner.RunAsync(cts.Token);
            trace = root.TraceId;
        }

        var command = Assert.Single(recorder.Snapshot(trace), a => a.OperationName == "cli.command");
        Assert.Equal("script:/cmd/echo.cmd.ts", command.GetTagItem(CliScriptingTags.CommandSource)?.ToString());
        Assert.Equal("echo", command.GetTagItem(CliScriptingTags.CommandName)?.ToString());
    }

    private sealed class TestRunner : InteractiveRunnerBase
    {
        public TestRunner(DefaultCommandRegistry d, ScriptCommandRegistry s, IConsoleAdapter c, IServiceProvider sp)
            : base(d, s, c, NullLogger<TestRunner>.Instance, sp) { }
        protected override string Banner => "(otel)";
        protected override string Prompt => "o> ";
    }
}
