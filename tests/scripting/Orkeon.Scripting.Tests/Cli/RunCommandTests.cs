using Orkeon.Scripting.Cli.Commands;

namespace Orkeon.Scripting.Tests.Cli;

/// <summary>
/// End-to-end tests for the <c>orkeon run</c> CLI: drive <see cref="RunCommand.ExecuteAsync"/>
/// in-process with synthesised <see cref="RunCommandOptions"/> and assert side-effects on the
/// scratch directory. Each test uses its own temp folder so they can run in parallel.
/// </summary>
public sealed class RunCommandTests : IDisposable
{
    private readonly string _root;
    private readonly string _scriptDir;
    private readonly string _outDir;

    public RunCommandTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ork-cli-" + Guid.NewGuid().ToString("N"));
        _scriptDir = Path.Combine(_root, "script");
        _outDir = Path.Combine(_root, "out");
        Directory.CreateDirectory(_scriptDir);
        Directory.CreateDirectory(_outDir);
        // Note: do NOT call Directory.SetCurrentDirectory here — xunit may run tests in
        // parallel and a shared-process cwd change races with sibling test classes.
        // The CLI's external-mount guard now exempts the script directory itself, so we
        // don't need to fake cwd anymore.
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ }
    }

    private string WriteScript(string fileName, string contents)
    {
        var path = Path.Combine(_scriptDir, fileName);
        File.WriteAllText(path, contents);
        return path;
    }

    [Fact]
    public async Task Cli_writes_output_via_file_write_tool_under_mount()
    {
        var scriptPath = WriteScript("hello.ork.ts", """
            /// <reference orkeon-script="1.0" />
            await tools.fileWrite({ path: "/output/hello.txt", content: "ok" });
            """);

        var exit = await RunCommand.ExecuteAsync(new RunCommandOptions
        {
            ScriptPath = scriptPath,
            Mounts = new[] { $"{_outDir}:/output:rw" },
        });

        Assert.Equal(0, exit);
        Assert.True(File.Exists(Path.Combine(_outDir, "hello.txt")));
        Assert.Equal("ok", await File.ReadAllTextAsync(Path.Combine(_outDir, "hello.txt"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Cli_returns_exit_1_when_script_missing()
    {
        var exit = await RunCommand.ExecuteAsync(new RunCommandOptions
        {
            ScriptPath = Path.Combine(_scriptDir, "does-not-exist.ork.ts"),
        });

        Assert.Equal(1, exit);
    }

    [Fact]
    public async Task Cli_bundles_relative_typescript_imports_from_entry_file()
    {
        // Proves the always-bundle mode: a helper exported from a sibling .ts file is
        // resolved by esbuild and reaches Jint as inlined code. Without bundling the
        // 'import' statement would survive into Jint script-mode and throw.
        var helperPath = WriteScript("helpers.ts", """
            export function greet(name: string): string {
                return `hello ${name}`;
            }
            """);

        var entryPath = WriteScript("main.ork.ts", """
            /// <reference orkeon-script="1.0" />
            import { greet } from "./helpers.ts";
            await tools.fileWrite({ path: "/output/out.txt", content: greet("orkeon") });
            """);

        var exit = await RunCommand.ExecuteAsync(new RunCommandOptions
        {
            ScriptPath = entryPath,
            Mounts = new[] { $"{_outDir}:/output:rw" },
        });

        Assert.Equal(0, exit);
        var outPath = Path.Combine(_outDir, "out.txt");
        Assert.True(File.Exists(outPath));
        Assert.Equal("hello orkeon", await File.ReadAllTextAsync(outPath, TestContext.Current.CancellationToken));
        // Keep the helper variable referenced so future readers know it's load-bearing.
        Assert.True(File.Exists(helperPath));
    }

    [Fact]
    public async Task Cli_runs_external_script_without_requiring_allow_flag()
    {
        // The script itself is the CLI's primary input — it is never gated behind
        // --allow-external-mounts even when it lives outside the cwd. Only user-declared
        // mounts (and --llm-log-path) require the opt-in. Same surface as YAML --config.
        var externalDir = Path.Combine(Path.GetTempPath(), "ork-ext-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(externalDir);
        var externalScript = Path.Combine(externalDir, "noop.ork.ts");
        await File.WriteAllTextAsync(externalScript, "/// <reference orkeon-script=\"1.0\" />\nvar result = 1;\n", TestContext.Current.CancellationToken);
        try
        {
            var exit = await RunCommand.ExecuteAsync(new RunCommandOptions
            {
                ScriptPath = externalScript,
            });
            Assert.Equal(0, exit);
        }
        finally
        {
            Directory.Delete(externalDir, recursive: true);
        }
    }

    [Fact]
    public async Task Cli_validate_resolves_script_defined_tools_through_the_pipeline()
    {
        // EX-01's central proof: a toolBuilder() tool attached to an agent survives
        // the adapter, gets registered with the runtime registry by the loader, and
        // passes CrewFactory's STRICT tool resolution — the exact spot that used to
        // fail with "unknown tool(s)".
        var scriptPath = WriteScript("crew.ork.ts", """
            /// <reference orkeon-script="1.0" />
            const indicator = toolBuilder()
                .name("spike_indicator")
                .description("Computes a number")
                .withSchema({ type: "object", properties: { n: { type: "number", description: "n" } }, required: ["n"] })
                .execute((input) => ({ doubled: input.n * 2 }))
                .build();
            const analyst = agentBuilder().name("analyst").role("Analyst").goal("Analyze")
                .tools(["json_tool"]).withAutonomousTool(indicator).build();
            const work = taskBuilder().agent(analyst).description("Analyze").expectedOutput("A result").build();
            const crew = crewBuilder().name("script-tools").goal("Prove first-class script tools")
                .withAgent(analyst).withTask(work).build();
            (globalThis as any).crew = crew;
            """);

        var exit = await RunCommand.ExecuteAsync(new RunCommandOptions
        {
            ScriptPath = scriptPath,
            Validate = true,
            // The validate path goes through the shared runner, whose workspace guard
            // refuses a script outside the test process' cwd without this opt-in.
            AllowExternalMounts = true,
        });

        Assert.Equal(0, exit);
    }

    [Fact]
    public async Task Cli_runs_a_declarative_crew_through_the_orchestration_pipeline()
    {
        // F6: a bare `orkeon run crew.ork.ts` on a script that hands off
        // `globalThis.crew` must take the FULL pipeline, not the flat agent loop.
        // The proof is the task deliverable: the flat loop ignores deliverables,
        // only the orchestration pipeline writes them.
        var scriptPath = WriteScript("crew.ork.ts", """
            /// <reference orkeon-script="1.0" />
            const writer = agentBuilder().name("writer").role("Writer").goal("Write a note").build();
            const note = taskBuilder()
                .agent(writer)
                .description("Write a one-line note")
                .expectedOutput("A note")
                .deliverable({ path: "/output/note.md", source: "final_message", format: "markdown" })
                .build();
            const crew = crewBuilder().name("declarative").goal("Prove the pipeline path")
                .withAgent(writer).withTask(note).build();
            (globalThis as any).crew = crew;
            """);

        // The pipeline's banner is the discriminator: the shared one-shot runner
        // prints "=== Crew Output ===", the flat script path prints a JSON result
        // blob. (The offline echo provider yields an empty final message, so the
        // deliverable file itself is not a reliable witness here.)
        var originalOut = Console.Out;
        using var captured = new StringWriter();
        Console.SetOut(captured);
        int exit;
        try
        {
            exit = await RunCommand.ExecuteAsync(new RunCommandOptions
            {
                ScriptPath = scriptPath,
                Mounts = new[] { $"{_outDir}:/output:rw" },
                AllowExternalMounts = true,
            });
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.Equal(0, exit);
        Assert.Contains("=== Crew Output ===", captured.ToString(), StringComparison.Ordinal);
    }
}
