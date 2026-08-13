using Orkeon.Scripting.Cli.Commands;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// Coverage for the directory dispatch of <see cref="RunCommand"/>: <c>orkeon run &lt;dir&gt;</c>
/// must load a multi-file YAML crew (<c>config.yaml</c> + <c>agents/</c> + <c>tasks/</c>, or the
/// flat legacy triplet) through the shared one-shot runner, honour the same options as a single
/// file, and refuse ambiguous or layout-less directories with a diagnostic naming what it found.
/// Driven in-process and offline like <see cref="RunCommandYamlTests"/>: no <c>Llm</c> section is
/// configured, so the crew finishes cleanly with empty output.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class RunCommandDirectoryTests
{
    private const string CrewSettings =
        """
        name: multifile-crew
        goal: Say hello deterministically
        process: sequential
        """;

    private const string GreeterAgent =
        """
        role: "Greeter"
        goal: "Greet"
        backstory: "A minimal agent for smoke testing."
        """;

    private const string GreetTask =
        """
        description: "Say hello."
        expected_output: "A greeting."
        agent: greeter
        """;

    /// <summary>Writes a per-entity crew tree under the scratch root and returns its directory.</summary>
    private static string WritePerEntityCrew(ScriptScratch scratch, string settingsFileName = "config.yaml")
    {
        var dir = Path.Combine(scratch.Root, "crew");
        Directory.CreateDirectory(Path.Combine(dir, "agents"));
        Directory.CreateDirectory(Path.Combine(dir, "tasks"));
        File.WriteAllText(Path.Combine(dir, settingsFileName), CrewSettings);
        File.WriteAllText(Path.Combine(dir, "agents", "greeter.yaml"), GreeterAgent);
        File.WriteAllText(Path.Combine(dir, "tasks", "greet.yaml"), GreetTask);
        return dir;
    }

    /// <summary>Writes the flat legacy triplet under the scratch root and returns its directory.</summary>
    private static string WriteFlatCrew(ScriptScratch scratch)
    {
        var dir = Path.Combine(scratch.Root, "flat");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "crew.yaml"), CrewSettings);
        File.WriteAllText(Path.Combine(dir, "agents.yaml"), "greeter:\n" + Indent(GreeterAgent));
        File.WriteAllText(Path.Combine(dir, "tasks.yaml"), "greet:\n" + Indent(GreetTask));
        return dir;
    }

    /// <summary>Creates <paramref name="directoryName"/> under the scratch root, empty.</summary>
    private static string CreateEmptyDirectory(ScriptScratch scratch, string directoryName)
    {
        var dir = Path.Combine(scratch.Root, directoryName);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Drops a trivial scripting entry point into <paramref name="dir"/> and returns its path.</summary>
    private static string WriteScriptInto(string dir)
    {
        var script = Path.Combine(dir, "crew.ork.ts");
        File.WriteAllText(script, "/// <reference orkeon-script=\"1.0\" />\nvar result = 42;\n");
        return script;
    }

    /// <summary>Writes a complete single-file crew inside <paramref name="dir"/> and returns its path.</summary>
    private static string WriteSingleFileCrewInto(string dir)
    {
        var path = Path.Combine(dir, "standalone.yaml");
        File.WriteAllText(path,
            """
            name: single-file-crew
            goal: Say hello deterministically
            process: sequential

            agents:
              greeter:
                role: "Greeter"
                goal: "Greet"
                backstory: "A minimal agent for smoke testing."

            tasks:
              greet:
                description: "Say hello."
                expected_output: "A greeting."
                agent: greeter
            """);
        return path;
    }

    [Theory]
    [InlineData("config.yaml")]
    [InlineData("crew.yaml")]
    public async Task PerEntity_directory_runs_end_to_end_and_returns_ExitOk(string settingsFileName)
    {
        using var scratch = new ScriptScratch();
        var dir = WritePerEntityCrew(scratch, settingsFileName);
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(new RunCommandOptions
        {
            ScriptPath = dir,
            // The scratch dir lives under the temp path (outside the test cwd), so the
            // runner's external-config guard requires the explicit opt-in.
            AllowExternalMounts = true,
        });

        Assert.Equal(Program.ExitOk, exit);
        // The one-shot YAML runner prints a "=== Crew Output ===" banner; the script path
        // never does. Asserting on it pins the routing via observable behavior.
        Assert.Contains("=== Crew Output ===", console.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Flat_triplet_directory_still_runs_end_to_end()
    {
        using var scratch = new ScriptScratch();
        var dir = WriteFlatCrew(scratch);
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(new RunCommandOptions
        {
            ScriptPath = dir,
            AllowExternalMounts = true,
        });

        Assert.Equal(Program.ExitOk, exit);
        Assert.Contains("=== Crew Output ===", console.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Validate_on_a_directory_reports_the_loaded_agents_and_tasks()
    {
        using var scratch = new ScriptScratch();
        var dir = WritePerEntityCrew(scratch);
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(new RunCommandOptions
        {
            ScriptPath = dir,
            AllowExternalMounts = true,
            Validate = true,
        });

        Assert.Equal(Program.ExitOk, exit);
        Assert.Contains("VALIDATION OK", console.Stdout, StringComparison.Ordinal);
        Assert.Contains("agents=1", console.Stdout, StringComparison.Ordinal);
        Assert.Contains("tasks=1", console.Stdout, StringComparison.Ordinal);
        // No kickoff happens during validation.
        Assert.DoesNotContain("=== Crew Output ===", console.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Directory_target_honours_the_shared_runner_options()
    {
        using var scratch = new ScriptScratch();
        var dir = WritePerEntityCrew(scratch);
        using var console = new TestConsole();

        // --settings / -V / --initial-context / --mount must behave exactly as on a file target.
        var settings = scratch.WriteFile("appsettings.json", "{}");
        var exit = await RunCommand.ExecuteAsync(new RunCommandOptions
        {
            ScriptPath = dir,
            AllowExternalMounts = true,
            SettingsPath = settings,
            Variables = ["TOPIC=orkeon"],
            InitialContext = "greet the user",
            Mounts = [$"{scratch.OutDir}:/output:rw"],
        });

        Assert.Equal(Program.ExitOk, exit);
        Assert.Contains("=== Crew Output ===", console.Stdout, StringComparison.Ordinal);
        Assert.Contains(settings, console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Directory_holding_both_a_yaml_layout_and_a_script_is_rejected_naming_both()
    {
        using var scratch = new ScriptScratch();
        var dir = WritePerEntityCrew(scratch);
        var script = WriteScriptInto(dir);
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(new RunCommandOptions
        {
            ScriptPath = dir,
            AllowExternalMounts = true,
        });

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("Ambiguous crew directory", console.Stderr, StringComparison.Ordinal);
        // Both candidates must be named — no silent precedence.
        Assert.Contains(Path.Combine(dir, "agents"), console.Stderr, StringComparison.Ordinal);
        Assert.Contains(script, console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Directory_without_a_recognized_layout_lists_what_was_searched()
    {
        using var scratch = new ScriptScratch();
        var dir = CreateEmptyDirectory(scratch, "not-a-crew");
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(new RunCommandOptions
        {
            ScriptPath = dir,
            AllowExternalMounts = true,
        });

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("no recognized crew layout", console.Stderr, StringComparison.Ordinal);
        Assert.Contains("'agents/'", console.Stderr, StringComparison.Ordinal);
        Assert.Contains("'tasks/'", console.Stderr, StringComparison.Ordinal);
        Assert.Contains("crew.yaml + agents.yaml + tasks.yaml", console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Directory_holding_only_a_script_points_at_the_file_instead_of_running_it()
    {
        using var scratch = new ScriptScratch();
        var dir = CreateEmptyDirectory(scratch, "script-only");
        var script = WriteScriptInto(dir);
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(new RunCommandOptions
        {
            ScriptPath = dir,
            AllowExternalMounts = true,
        });

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains(script, console.Stderr, StringComparison.Ordinal);
        // The script must never run implicitly from a directory target.
        Assert.DoesNotContain("\"result\"", console.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Yaml_file_inside_a_crew_directory_still_dispatches_by_extension()
    {
        // Regression guard: the directory branch must not shadow the file dispatch. The single
        // file below is a complete crew and must run on its own, ignoring the sibling folders.
        using var scratch = new ScriptScratch();
        var dir = WritePerEntityCrew(scratch);
        var singleFile = WriteSingleFileCrewInto(dir);
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(new RunCommandOptions
        {
            ScriptPath = singleFile,
            AllowExternalMounts = true,
            Validate = true,
        });

        Assert.Equal(Program.ExitOk, exit);
        Assert.Contains($"VALIDATION OK: {singleFile}", console.Stdout, StringComparison.Ordinal);
    }

    /// <summary>Indents a YAML block so it can be nested under a dictionary key.</summary>
    private static string Indent(string block)
        => string.Join('\n', block.Split('\n').Select(line => "  " + line));
}
