using Orkeon.Hosting.Tests.Doubles;
using Orkeon.Scripting.Toolchain;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// <c>orkeon run --validate</c> on a <c>.ork.ts</c> with the real TypeScript compiler (FWK-12).
/// Skipped when Node or the bootstrapped <c>tsc</c> (<c>tools/scripting-typecheck</c>) is missing.
/// </summary>
[Trait("Category", "Slow")]
[Collection(ConsoleSerialCollection.Name)]
public sealed class ValidateTypeCheckTests : IDisposable
{
    private sealed class TestOptions : RunnerOptionsBase { }

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "orkeon-validate-typecheck-" + Guid.NewGuid().ToString("N"));

    public ValidateTypeCheckTests() => Directory.CreateDirectory(Path.Combine(_dir, "lib"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* best-effort temp cleanup */ }
        catch (UnauthorizedAccessException) { /* best-effort temp cleanup */ }
    }

    private static bool CompilerAvailable()
    {
        try
        {
            _ = new TscTypeChecker().ResolveBinary();
            return true;
        }
        catch (TscNotFoundException)
        {
            return false;
        }
    }

    private async Task<(int Exit, string Err)> ValidateAsync(string source, params (string Path, string Text)[] extra)
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "appsettings.json"), "{ \"RaggableTree\": { \"Enabled\": false } }");
        await File.WriteAllTextAsync(Path.Combine(_dir, "lib", "helper.ts"), "export const label: string = 42;\n");
        foreach (var (path, text) in extra)
            await File.WriteAllTextAsync(Path.Combine(_dir, path), text);
        var script = Path.Combine(_dir, "main.ork.ts");
        await File.WriteAllTextAsync(script, source);

        var console = new FakeRunnerConsole();
        var exit = await RunnerExecution.RunValidateAsync(
            new TestOptions { ConfigPath = script, AllowExternalMounts = true, RunnerConsole = console },
            "Orkeon.Hosting.Tests");
        return (exit, console.ErrorOutput.ToString());
    }

    [Fact]
    public async Task A_type_error_in_the_script_fails_validate_with_its_virtual_position()
    {
        Assert.SkipUnless(CompilerAvailable(), "tsc is not bootstrapped (npm --prefix tools/scripting-typecheck install --prefer-offline).");

        var (exit, err) = await ValidateAsync("/// <reference orkeon-script=\"1.0\" />\nconst n: number = \"a\";\n");

        Assert.Equal(1, exit);
        Assert.Contains("VALIDATION FAILED: type errors", err, StringComparison.Ordinal);
        Assert.Contains("/script/main.ork.ts:2:7 TS2322", err, StringComparison.Ordinal);
        Assert.DoesNotContain(_dir, err.Replace("Using settings: " + Path.Combine(_dir, "appsettings.json"), string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_type_error_in_an_imported_file_names_it_by_a_virtual_path()
    {
        Assert.SkipUnless(CompilerAvailable(), "tsc is not bootstrapped (npm --prefix tools/scripting-typecheck install --prefer-offline).");

        var (exit, err) = await ValidateAsync("/// <reference orkeon-script=\"1.0\" />\nimport { label } from \"./lib/helper.ts\";\nconst s: string = label;\n");

        Assert.Equal(1, exit);
        Assert.Contains("/script/lib/helper.ts:1:14 TS2322", err, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_pragma_in_an_imported_module_does_not_hide_its_type_errors()
    {
        Assert.SkipUnless(CompilerAvailable(), "tsc is not bootstrapped (npm --prefix tools/scripting-typecheck install --prefer-offline).");

        var (exit, err) = await ValidateAsync(
            "/// <reference orkeon-script=\"1.0\" />\nimport { label } from \"./lib/pragma.ts\";\nconst s: string = label;\n",
            ("lib/pragma.ts", "/// <reference orkeon-script=\"1.0\" />\nexport const label: string = 42;\n"));

        Assert.Equal(1, exit);
        Assert.DoesNotContain("TS1084", err, StringComparison.Ordinal);
        Assert.Contains("/script/lib/pragma.ts:2:14 TS2322", err, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_test_global_of_the_typings_is_known()
    {
        Assert.SkipUnless(CompilerAvailable(), "tsc is not bootstrapped (npm --prefix tools/scripting-typecheck install --prefer-offline).");

        var (_, err) = await ValidateAsync("/// <reference orkeon-script=\"1.0\" />\ntest.mockLlm.when(\"a\").respond(\"b\");\n");

        Assert.DoesNotContain("type errors", err, StringComparison.Ordinal);
    }
}
