using System.Diagnostics;
using Orkeon.Scripting.Configuration;
using Orkeon.Scripting.Toolchain;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Scripting.Tests.Toolchain;

/// <summary>
/// How <see cref="TscTypeChecker"/> treats the compiler process (FWK-12): a caller's cancellation
/// kills it, an exit with no diagnostic is a failure, and no message carries a physical path. The
/// compiler is a hand-written shell script, so these judge the checker, not tsc.
/// </summary>
public sealed class TscTypeCheckerProcessTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "orkeon-tsc-process-" + Guid.NewGuid().ToString("N"));
    private string _script = string.Empty;

    public TscTypeCheckerProcessTests()
    {
        Directory.CreateDirectory(_dir);
        _script = Path.Combine(_dir, "main.ork.ts");
        File.WriteAllText(_script, "const n = 1;\n");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* best-effort temp cleanup */ }
        catch (UnauthorizedAccessException) { /* best-effort temp cleanup */ }
    }

    private TscTypeChecker CheckerWith(string shellBody)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the fake compiler is a shell script");
        var path = Path.Combine(_dir, "fake-tsc.sh");
        File.WriteAllText(path, "#!/bin/sh\n" + shellBody + "\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return new TscTypeChecker(new ScriptingToolchainOptions { TscPath = path });
    }

    private static async Task<string> WaitForFileAsync(string path)
    {
        // The fake compiler writes the file line by line: a file that does not end its line yet is still being written.
        string? text = null;
        await Polling.WaitUntilAsync(async () =>
        {
            if (!File.Exists(path))
                return false;
            text = await File.ReadAllTextAsync(path);
            return text.EndsWith('\n');
        }, TimeSpan.FromSeconds(30));
        return text!.Trim();
    }

    [Fact]
    public async Task Cancelling_the_caller_kills_the_compiler_and_removes_the_scratch_folder()
    {
        var pidFile = Path.Combine(_dir, "pid");
        var configFile = Path.Combine(_dir, "config");
        var checker = CheckerWith($"echo $$ > '{pidFile}'\necho \"$2\" > '{configFile}'\nexec sleep 600");
        using var cts = new CancellationTokenSource();

        var check = checker.CheckAsync(_script, "/script/main.ork.ts", cts.Token);
        var pid = int.Parse(await WaitForFileAsync(pidFile), System.Globalization.CultureInfo.InvariantCulture);
        var scratch = Path.GetDirectoryName(await WaitForFileAsync(configFile))!;
        Assert.True(Directory.Exists(scratch));

        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => check);

        // Polling throws when tsc is still running after the caller cancelled.
        await Polling.WaitUntilAsync(() => ProcessHasExited(pid));
        Assert.False(Directory.Exists(scratch), "the scratch folder was left behind");
    }

    private static bool ProcessHasExited(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.HasExited;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    [Fact]
    public async Task A_non_zero_exit_with_no_diagnostic_is_a_failure_never_no_errors()
    {
        var checker = CheckerWith("exit 3");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => checker.CheckAsync(_script, "/script/main.ork.ts", TestContext.Current.CancellationToken));

        Assert.StartsWith("type check failed (exit 3)", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_crash_message_carries_the_output_with_no_physical_path()
    {
        var checker = CheckerWith($"echo \"FATAL ERROR at {_dir}/main.ork.ts\" >&2\nexit 134");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => checker.CheckAsync(_script, "/script/main.ork.ts", TestContext.Current.CancellationToken));

        Assert.StartsWith("type check failed (exit 134)", ex.Message, StringComparison.Ordinal);
        Assert.Contains("FATAL ERROR at /script/main.ork.ts", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(_dir, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failure_before_the_compiler_runs_names_the_script_by_its_virtual_path()
    {
        var checker = CheckerWith("exit 0");
        var missing = Path.Combine(_dir, "gone", "main.ork.ts");

        var ex = await Assert.ThrowsAnyAsync<Exception>(
            () => checker.CheckAsync(missing, "/script/main.ork.ts", TestContext.Current.CancellationToken));

        Assert.IsNotType<OperationCanceledException>(ex);
        Assert.Contains("/script/main.ork.ts", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(_dir, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_diagnostic_with_a_non_zero_exit_is_still_a_diagnostic()
    {
        var checker = CheckerWith("echo \"$(grep -o '\"[^\"]*main\\.ork\\.ts\"' \"$2\" | tr -d '\"')(1,7): error TS2322: bad.\"\nexit 2");

        var diagnostics = await checker.CheckAsync(_script, "/script/main.ork.ts", TestContext.Current.CancellationToken);

        var d = Assert.Single(diagnostics);
        Assert.Equal("/script/main.ork.ts:1:7 TS2322 bad.", d.ToString());
    }

    [Fact]
    public async Task A_node_warning_on_an_exit_0_run_is_not_a_diagnostic()
    {
        var checker = CheckerWith("echo '(node:42) [DEP0040] DeprecationWarning: The punycode module is deprecated.' >&2\nexit 0");

        var diagnostics = await checker.CheckAsync(_script, "/script/main.ork.ts", TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task A_grandchild_holding_the_pipes_does_not_hang_the_check()
    {
        var checker = CheckerWith("sleep 30 &\necho 'partial' \nexit 0");

        var diagnostics = await checker.CheckAsync(_script, "/script/main.ork.ts", TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(25), TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task A_failure_tail_redacts_an_absolute_path_it_cannot_map()
    {
        var checker = CheckerWith("echo 'FATAL at /usr/lib/node_modules/typescript/lib/tsc.js:12 and /script/lib.ts' >&2\nexit 134");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => checker.CheckAsync(_script, "/script/main.ork.ts", TestContext.Current.CancellationToken));

        Assert.DoesNotContain("/usr/lib", ex.Message, StringComparison.Ordinal);
        Assert.Contains("<path>", ex.Message, StringComparison.Ordinal);
    }

    // #315: tsc reports a clash (TS6200) on the file named, and only while the roll-up is in the program.
    private TscTypeChecker CheckerClashingWith(string file)
        => CheckerWith($"if grep -q 'orkeon-typecheck-[0-9a-f]*/orkeon\\.d\\.ts\"' \"$2\"; then echo '{file}(13,1): error TS6200: Definitions conflict.'; exit 2; fi\nexit 0");

    private static string Rollup()
    {
        using var stream = typeof(TscTypeChecker).Assembly.GetManifestResourceStream("Orkeon.Scripting.Typings.orkeon.d.ts")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private string WriteTypingsCopy(string text)
    {
        Directory.CreateDirectory(Path.Combine(_dir, ".orkeon"));
        var copy = Path.Combine(_dir, ".orkeon", "orkeon.d.ts");
        File.WriteAllText(copy, text);
        return copy;
    }

    [Fact]
    public async Task A_referenced_copy_of_this_builds_typings_stands_in_for_the_rollup()
    {
        var checker = CheckerClashingWith(WriteTypingsCopy(Rollup().Replace("\n", "\r\n", StringComparison.Ordinal)));

        var diagnostics = await checker.CheckAsync(_script, "/script/main.ork.ts", TestContext.Current.CancellationToken);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task A_referenced_stale_copy_of_the_typings_is_named_with_the_command_that_regenerates_it()
    {
        var rollup = Rollup();
        var header = rollup[..rollup.IndexOf('\n', StringComparison.Ordinal)];
        var checker = CheckerClashingWith(WriteTypingsCopy(header + "\ndeclare const stale: number;\n"));

        var diagnostics = await checker.CheckAsync(_script, "/script/main.ork.ts", TestContext.Current.CancellationToken);

        var d = Assert.Single(diagnostics);
        Assert.Equal("/script/.orkeon/orkeon.d.ts", d.Path);
        Assert.Contains("orkeon typings", d.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_clash_with_a_file_that_is_not_the_typings_is_reported_as_tsc_wrote_it()
    {
        var other = Path.Combine(_dir, "globals.d.ts");
        await File.WriteAllTextAsync(other, "declare const label: string;\n", TestContext.Current.CancellationToken);
        var checker = CheckerClashingWith(other);

        var diagnostics = await checker.CheckAsync(_script, "/script/main.ork.ts", TestContext.Current.CancellationToken);

        var d = Assert.Single(diagnostics);
        Assert.Equal("/script/globals.d.ts:13:1 TS6200 Definitions conflict.", d.ToString());
    }
}
