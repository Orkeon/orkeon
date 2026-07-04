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
}
