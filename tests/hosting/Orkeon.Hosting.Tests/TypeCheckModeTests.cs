using System.Text.Json;
using Orkeon.Hosting.Tests.Doubles;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// The modes of <c>Orkeon:Scripting:Toolchain:TypeCheck</c> (FWK-12), through the real
/// <see cref="RunnerExecution.RunValidateAsync"/>. The compiler is a hand-written shell script
/// (<c>TscPath</c>) that reports one error on the file named by the generated tsconfig, so the
/// tests judge the runner's wiring, not tsc; the real compiler is
/// <c>ValidateTypeCheckTests</c>.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class TypeCheckModeTests : IDisposable
{
    private sealed class TestOptions : RunnerOptionsBase { }

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "orkeon-typecheck-mode-" + Guid.NewGuid().ToString("N"));

    public TypeCheckModeTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* best-effort temp cleanup */ }
        catch (UnauthorizedAccessException) { /* best-effort temp cleanup */ }
    }

    /// <summary>A tsc that dies (exit 3) after printing a physical path, with no diagnostic.</summary>
    private string CrashingTsc()
    {
        var path = Path.Combine(_dir, "crashing-tsc.sh");
        File.WriteAllText(path, $"#!/bin/sh\necho \"FATAL at {_dir}/main.ork.ts\" >&2\nexit 3\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    /// <summary>A tsc that fails with one error on the script named by its tsconfig.</summary>
    private string FakeTsc()
    {
        var path = Path.Combine(_dir, "fake-tsc.sh");
        File.WriteAllText(path, """
            #!/bin/sh
            file=$(grep -o '"[^"]*\.ork\.ts"' "$2" | tr -d '"')
            echo "$file(2,7): error TS2322: Type 'string' is not assignable to type 'number'."
            exit 2
            """.Replace("\r\n", "\n", StringComparison.Ordinal));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    private async Task<(int Exit, string Err)> ValidateAsync(string typeCheck, string tscPath)
    {
        await File.WriteAllTextAsync(
            Path.Combine(_dir, "appsettings.json"),
            JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["RaggableTree"] = new { Enabled = false },
                ["Orkeon"] = new { Scripting = new { Toolchain = new { TypeCheck = typeCheck, TscPath = tscPath } } },
            }));
        var script = Path.Combine(_dir, "main.ork.ts");
        await File.WriteAllTextAsync(script, "/// <reference orkeon-script=\"1.0\" />\nconst n: number = \"a\";\n");

        var console = new FakeRunnerConsole();
        var exit = await RunnerExecution.RunValidateAsync(
            new TestOptions { ConfigPath = script, AllowExternalMounts = true, RunnerConsole = console },
            "Orkeon.Hosting.Tests");
        // "Using settings: <path>" is the host's own announcement of the operator's file, not type-check output.
        var err = string.Join('\n', console.ErrorOutput.ToString()
            .Split('\n').Where(l => !l.StartsWith("Using settings:", StringComparison.Ordinal)));
        return (exit, err);
    }

    [Theory]
    [InlineData("Validate")]
    [InlineData("Always")]
    public async Task A_type_error_fails_validate_with_virtual_paths_only(string mode)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the fake compiler is a shell script");

        var (exit, err) = await ValidateAsync(mode, FakeTsc());

        Assert.Equal(1, exit);
        Assert.Contains("VALIDATION FAILED: type errors", err, StringComparison.Ordinal);
        Assert.Contains("/script/main.ork.ts:2:7 TS2322 Type 'string' is not assignable to type 'number'.", err, StringComparison.Ordinal);
        Assert.DoesNotContain(_dir, err, StringComparison.Ordinal);
        Assert.DoesNotContain(Path.GetTempPath(), err, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Off_never_runs_the_compiler()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the fake compiler is a shell script");

        var (_, err) = await ValidateAsync("Off", FakeTsc());

        Assert.DoesNotContain("type errors", err, StringComparison.Ordinal);
        Assert.DoesNotContain("type check", err, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_compiler_is_a_warning_under_validate_and_validation_goes_on()
    {
        var (_, err) = await ValidateAsync("Validate", Path.Combine(_dir, "no-such-tsc"));

        Assert.Contains("WARNING: type check skipped: TypeScript compiler not found", err, StringComparison.Ordinal);
        Assert.DoesNotContain("VALIDATION FAILED: type check unavailable", err, StringComparison.Ordinal);
        var warning = err.Split('\n').Single(l => l.StartsWith("WARNING: type check skipped", StringComparison.Ordinal));
        Assert.DoesNotContain(_dir, warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_compiler_fails_validate_under_always()
    {
        var (exit, err) = await ValidateAsync("Always", Path.Combine(_dir, "no-such-tsc"));

        Assert.Equal(1, exit);
        Assert.Contains("VALIDATION FAILED: type check unavailable", err, StringComparison.Ordinal);
        Assert.Contains("TypeScript compiler not found", err, StringComparison.Ordinal);
        Assert.Contains("TscPath", err, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_compiler_that_dies_fails_validate_and_its_output_is_redacted()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the fake compiler is a shell script");

        var (exit, err) = await ValidateAsync("Validate", CrashingTsc());

        Assert.Equal(1, exit);
        Assert.Contains("VALIDATION FAILED: type check failed (exit 3)", err, StringComparison.Ordinal);
        Assert.Contains("FATAL at /script/main.ork.ts", err, StringComparison.Ordinal);
        var lines = err.Split('\n').Where(l => l.StartsWith("FATAL", StringComparison.Ordinal) || l.StartsWith("VALIDATION FAILED: type check", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains(_dir, StringComparison.Ordinal));
    }
}
