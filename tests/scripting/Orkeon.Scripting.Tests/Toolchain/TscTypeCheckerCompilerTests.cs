using System.Text.Json;
using Orkeon.Scripting.Configuration;
using Orkeon.Scripting.Toolchain;

namespace Orkeon.Scripting.Tests.Toolchain;

/// <summary>
/// <see cref="TscTypeChecker"/> with the compiler <c>tools/scripting-typecheck</c> pins (TypeScript 7 since
/// #346), on a module that references the project's copy of the typings (#315, #424). CI's fast suite
/// bootstraps that compiler before it runs; these skip where it is not bootstrapped.
/// </summary>
public sealed class TscTypeCheckerCompilerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "orkeon-tsc-compiler-" + Guid.NewGuid().ToString("N"));

    public TscTypeCheckerCompilerTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "lib"));
        Directory.CreateDirectory(Path.Combine(_dir, ".orkeon"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* best-effort temp cleanup */ }
        catch (UnauthorizedAccessException) { /* best-effort temp cleanup */ }
    }

    private static string? FindUp(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate) || Directory.Exists(candidate))
                return candidate;
        }
        return null;
    }

    /// <summary>The pinned compiler, checked to be the version <c>package.json</c> pins; skips when not bootstrapped.</summary>
    private static TscTypeChecker PinnedChecker()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the bootstrapped tsc is run through its node shim");
        var tool = FindUp(Path.Combine("tools", "scripting-typecheck"));
        Assert.True(tool is not null, "tools/scripting-typecheck not found above the test output.");
        var tsc = Path.Combine(tool!, "node_modules", ".bin", "tsc");
        Assert.SkipUnless(File.Exists(tsc), "tsc is not bootstrapped: npm --prefix tools/scripting-typecheck install --prefer-offline");

        using var pin = JsonDocument.Parse(File.ReadAllText(Path.Combine(tool!, "package.json")));
        using var installed = JsonDocument.Parse(File.ReadAllText(Path.Combine(tool!, "node_modules", "typescript", "package.json")));
        Assert.Equal(
            pin.RootElement.GetProperty("dependencies").GetProperty("typescript").GetString(),
            installed.RootElement.GetProperty("version").GetString());

        return new TscTypeChecker(new ScriptingToolchainOptions { TscPath = tsc });
    }

    private async Task<IReadOnlyList<TypeCheckDiagnostic>> CheckModuleReferencingCopyAsync(string copy, string module)
    {
        var checker = PinnedChecker();
        var ct = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(Path.Combine(_dir, ".orkeon", "orkeon.d.ts"), copy, ct);
        await File.WriteAllTextAsync(Path.Combine(_dir, "lib", "infra.ts"),
            "/// <reference path=\"../.orkeon/orkeon.d.ts\" />\n" + module, ct);
        var script = Path.Combine(_dir, "main.ork.ts");
        await File.WriteAllTextAsync(script,
            "// @orkeon-script 1.0\nimport { label } from \"./lib/infra.ts\";\nconst s: string = label;\n", ct);

        return await checker.CheckAsync(script, "/script/main.ork.ts", ct);
    }

    [Fact]
    [Trait("BR", "CH-01")]
    public async Task A_module_referencing_a_current_copy_of_the_typings_checks_clean()
    {
        var diagnostics = await CheckModuleReferencingCopyAsync(TypingsRollup.Read(), "export const label: string = \"ok\";\n");

        Assert.Empty(diagnostics);
    }

    [Fact]
    [Trait("BR", "CH-01")]
    public async Task A_type_error_in_a_module_referencing_the_copy_is_still_reported()
    {
        var diagnostics = await CheckModuleReferencingCopyAsync(TypingsRollup.Read(), "export const label: string = 42;\n");

        var d = Assert.Single(diagnostics);
        Assert.Equal("/script/lib/infra.ts", d.Path);
        Assert.Equal((2, 14, "TS2322"), (d.Line, d.Column, d.Code));
    }

    [Fact]
    [Trait("BR", "CH-01")]
    public async Task A_stale_copy_of_the_typings_is_named_once_with_the_command_that_regenerates_it()
    {
        var diagnostics = await CheckModuleReferencingCopyAsync(
            TypingsRollup.Read() + "\ndeclare const staleMarker: number;\n", "export const label: string = \"ok\";\n");

        var d = Assert.Single(diagnostics);
        Assert.Equal("/script/.orkeon/orkeon.d.ts", d.Path);
        Assert.Contains("orkeon typings", d.Message, StringComparison.Ordinal);
    }
}
