using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Scripting.Cli.Commands;
using Orkeon.Scripting.Cli.Tests.Doubles;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// End-to-end coverage for <see cref="RunCommand"/> driven in-process. Scripts are pure
/// procedural .ork.ts (no LLM call), so the full host bootstrap, esbuild bundling and Jint
/// execution run offline and deterministically.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class RunCommandTests
{
    private const string ResultOne =
        "/// <reference orkeon-script=\"1.0\" />\nvar result = 42;\n";

    [Fact]
    public void ExecuteAsync_with_null_options_throws_ArgumentNullException()
    {
        // Validation is eager (synchronous) so a null argument surfaces at the call site
        // rather than being captured inside the returned Task. Wrap in an Action so the
        // synchronous throw is observed (not the async overload of Assert.Throws).
        Action act = () => _ = RunCommand.ExecuteAsync(Orkeon.Hosting.SystemRunnerConsole.Instance, null!);
        Assert.Throws<ArgumentNullException>(act);
    }

    [Fact]
    [Trait("BR", "CH-11")]
    public async Task ShouldRefuseBeforeTheHost_WhenAYamlCrewFolderIsOutsideTheWorkingDirectory()
    {
        using var scratch = new ScriptScratch();
        var crew = scratch.WriteScript("crew.yaml", """
            name: outside-crew
            agents:
              - name: a
                role: r
                goal: g
            tasks:
              - name: t
                description: d
                agent: a
            """);
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(console, new RunCommandOptions
        {
            ScriptPath = crew,
        });

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("--allow-external-mounts is required", console.Stderr, StringComparison.Ordinal);
        Assert.Contains(scratch.ScriptDir, console.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("Using settings", console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-11")]
    public async Task ShouldRefuseTheMountButReadTheScript_WhenAProceduralScriptRunsFromElsewhere()
    {
        using var scratch = new ScriptScratch();
        using var other = new ScriptScratch();
        await File.WriteAllTextAsync(Path.Combine(scratch.ScriptDir, "data.txt"), "payload-ok", TestContext.Current.CancellationToken);
        var script = scratch.WriteScript("read.ork.ts", """
            /// <reference orkeon-script="1.0" />
            await tools.fileRead({ path: "/script/data.txt" });
            """);

        int refused;
        string refusedErr;
        using (var console = new TestConsole())
        {
            refused = await RunCommand.ExecuteAsync(console, new RunCommandOptions
            {
                ScriptPath = script,
                Mounts = new[] { $"{other.OutDir}:/data:ro" },
            });
            refusedErr = console.Stderr;
        }

        Assert.NotEqual(Program.ExitOk, refused);
        Assert.Contains(other.OutDir, refusedErr, StringComparison.Ordinal);
        Assert.Contains("--allow-external-mounts", refusedErr, StringComparison.Ordinal);

        using var console2 = new TestConsole();
        var exit = await RunCommand.ExecuteAsync(console2, new RunCommandOptions { ScriptPath = script });

        // A denied read throws in the script and turns the exit code: ExitOk is the read.
        Assert.Equal(Program.ExitOk, exit);
        Assert.DoesNotContain("denied", console2.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Missing_script_returns_ExitScriptError_and_reports_on_stderr()
    {
        using var scratch = new ScriptScratch();
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(console, new RunCommandOptions
        {
            ScriptPath = Path.Combine(scratch.ScriptDir, "nope.ork.ts"),
        });

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("script not found", console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Simple_script_returns_ExitOk_and_emits_result_JSON()
    {
        using var scratch = new ScriptScratch();
        var script = scratch.WriteScript("hello.ork.ts", ResultOne);
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(console, new RunCommandOptions
        {
            ScriptPath = script,
        });

        Assert.Equal(Program.ExitOk, exit);
        // The result payload is always serialized under a "result" key (ResultJsonOptions).
        Assert.Contains("\"result\"", console.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Script_writes_output_through_mounted_directory()
    {
        using var scratch = new ScriptScratch();
        var script = scratch.WriteScript("write.ork.ts", """
            /// <reference orkeon-script="1.0" />
            await tools.fileWrite({ path: "/output/out.txt", content: "ok" });
            """);
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(console, new RunCommandOptions
        {
            ScriptPath = script,
            Mounts = new[] { $"{scratch.OutDir}:/output:rw" },
            AllowExternalMounts = true,
        });

        Assert.Equal(Program.ExitOk, exit);
        var outPath = Path.Combine(scratch.OutDir, "out.txt");
        Assert.True(File.Exists(outPath));
        Assert.Equal("ok", await File.ReadAllTextAsync(outPath, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Verbose_levels_enable_logging_without_changing_exit(int verbose)
    {
        using var scratch = new ScriptScratch();
        var script = scratch.WriteScript("v.ork.ts", ResultOne);
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(console, new RunCommandOptions
        {
            ScriptPath = script,
            Verbose = verbose,
            Mounts = new[] { $"{scratch.OutDir}:/output:rw" },
            AllowExternalMounts = true,
        });

        Assert.Equal(Program.ExitOk, exit);
    }

    [Fact]
    public async Task Verbose_out_of_range_is_clamped_and_still_succeeds()
    {
        using var scratch = new ScriptScratch();
        var script = scratch.WriteScript("clamp.ork.ts", ResultOne);
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(console, new RunCommandOptions
        {
            ScriptPath = script,
            Verbose = 99, // Math.Clamp(_, 0, 2) keeps it at 2.
        });

        Assert.Equal(Program.ExitOk, exit);
    }

    [Fact]
    public async Task Explicit_missing_settings_emits_warning_and_still_runs()
    {
        using var scratch = new ScriptScratch();
        var script = scratch.WriteScript("s.ork.ts", ResultOne);
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(console, new RunCommandOptions
        {
            ScriptPath = script,
            SettingsPath = Path.Combine(scratch.Root, "does-not-exist.json"),
        });

        Assert.Equal(Program.ExitOk, exit);
        Assert.Contains("settings not found", console.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Explicit_existing_settings_is_used_and_reported()
    {
        using var scratch = new ScriptScratch();
        var script = scratch.WriteScript("s2.ork.ts", ResultOne);
        var settings = scratch.WriteFile("appsettings.json", "{}");
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(console, new RunCommandOptions
        {
            ScriptPath = script,
            SettingsPath = settings,
        });

        Assert.Equal(Program.ExitOk, exit);
        Assert.Contains("Using settings", console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Inputs_options_are_accepted_and_discarded()
    {
        using var scratch = new ScriptScratch();
        var script = scratch.WriteScript("in.ork.ts", ResultOne);
        var inputsFile = scratch.WriteFile("inputs.json", "{\"x\":1}");
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(console, new RunCommandOptions
        {
            ScriptPath = script,
            InputsJson = "{\"y\":2}",
            InputsFilePath = inputsFile,
        });

        Assert.Equal(Program.ExitOk, exit);
    }

    [Theory]
    [InlineData(64)]  // explicit positive cap
    [InlineData(0)]   // disables the cap (long.MaxValue)
    public async Task Memory_limit_override_is_applied(long limitMb)
    {
        using var scratch = new ScriptScratch();
        var script = scratch.WriteScript("mem.ork.ts", ResultOne);
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(console, new RunCommandOptions
        {
            ScriptPath = script,
            MemoryLimitMb = limitMb,
            Verbose = 1, // exercise the "memory limit overridden" log branch
        });

        Assert.Equal(Program.ExitOk, exit);
    }

    private const string EnabledSettings = "{\"Orkeon\":{\"Decision\":{\"Enabled\":true}}}";

    [Fact]
    [Trait("BR", "UC-04")]
    public async Task ShouldSwitchCircuitOff_WhenRunCommandGivenNoDecisionOption()
    {
        using var scratch = new ScriptScratch();
        var script = scratch.WriteScript("nd.ork.ts", ResultOne);
        var settings = scratch.WriteFile("appsettings.json", EnabledSettings);
        using var console = new TestConsole();
        var probe = new CircuitProbe();

        var exit = await RunCommand.ExecuteAsync(console, new RunCommandOptions
        {
            ScriptPath = script,
            SettingsPath = settings,
            NoDecision = true,
            ConfigureTestServices = probe.Configure,
        });

        Assert.Equal(Program.ExitOk, exit);
        Assert.NotNull(probe.Seen);
        Assert.False(probe.Seen.Enabled);
    }

    [Fact]
    [Trait("BR", "UC-04")]
    public async Task ShouldSwitchCircuitOff_WhenYamlCrewRunsWithNoDecisionOption()
    {
        using var scratch = new ScriptScratch();
        var crew = scratch.WriteScript("crew.yaml", """
            name: nd-crew
            agents:
              - name: a
                role: r
                goal: g
            tasks:
              - name: t
                description: d
                agent: a
            """);
        var settings = scratch.WriteFile("appsettings.json", EnabledSettings);
        using var console = new TestConsole();
        var probe = new CircuitProbe();

        // The run itself may fail later (no LLM): only the circuit the provider resolves is observed.
        _ = await RunCommand.ExecuteAsync(console, new RunCommandOptions
        {
            ScriptPath = crew,
            SettingsPath = settings,
            NoDecision = true,
            AllowExternalMounts = true,
            ConfigureTestServices = probe.Configure,
        });

        Assert.NotNull(probe.Seen);
        Assert.False(probe.Seen.Enabled);
    }

    private sealed class CircuitProbe
    {
        public DecisionCircuit? Seen { get; private set; }

        public void Configure(Microsoft.Extensions.Hosting.HostBuilderContext ctx, IServiceCollection services)
        {
            services.AddSingleton<IDecisionModel, FakeDecisionModel>();
            // The run resolves ILoggerFactory from its provider: reading the circuit there observes the provider's answer.
            services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(sp =>
            {
                Seen = sp.GetRequiredService<DecisionCircuit>();
                return new Microsoft.Extensions.Logging.LoggerFactory();
            });
        }
    }
}
