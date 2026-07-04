using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Abstractions.Runners;
using Orkeon.Cli.Commands;
using Orkeon.Cli.Registry;
using Orkeon.Cli.Scripting.Loading;
using Orkeon.Cli.Scripting.Registry;
using Orkeon.Cli.Scripting.Telemetry;
using Orkeon.Cli.Scripting.Tests.Fixtures;
using Orkeon.Scripting;
using Orkeon.Scripting.Telemetry;
using Orkeon.Scripting.Toolchain;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Cli.Scripting.Tests.Telemetry;

/// <summary>
/// Verifies the spec §10 commitment: every scripted command invocation emits an
/// <see cref="Activity"/> tagged with the source script's virtual path.
/// </summary>
public sealed class ActivityTagTests : IDisposable
{
    private readonly string _tempDir;
    private readonly List<Activity> _activities = new();
    private readonly ActivityListener _listener;

    public ActivityTagTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "orkeon-cli-scripting-otel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "echo-args.cmd.ts"),
            Path.Combine(_tempDir, "echo.cmd.ts"));

        _listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == ScriptingActivitySource.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = a => { lock (_activities) _activities.Add(a); },
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        _listener.Dispose();
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

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await runner.RunAsync(cts.Token);

        // ActivitySource is process-global; other concurrent tests may emit "cli.command"
        // spans too. Match on the source tag we expect to single ours out.
        var tagged = _activities.FirstOrDefault(a =>
            a.OperationName == "cli.command" &&
            "script:/cmd/echo.cmd.ts".Equals(a.GetTagItem(CliScriptingTags.CommandSource)?.ToString(), StringComparison.Ordinal));
        Assert.NotNull(tagged);
        Assert.Equal("echo", tagged!.GetTagItem(CliScriptingTags.CommandName)?.ToString());
    }

    private sealed class TestRunner : InteractiveRunnerBase
    {
        public TestRunner(DefaultCommandRegistry d, ScriptCommandRegistry s, IConsoleAdapter c, IServiceProvider sp)
            : base(d, s, c, NullLogger<TestRunner>.Instance, sp) { }
        protected override string Banner => "(otel)";
        protected override string Prompt => "o> ";
    }
}
