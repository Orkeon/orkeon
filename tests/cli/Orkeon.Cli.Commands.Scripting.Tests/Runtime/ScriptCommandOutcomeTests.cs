using System.Collections.Immutable;
using Orkeon.Cli.Abstractions.Commands;
using Orkeon.Cli.Commands.Scripting.Loading;
using Orkeon.Cli.Commands.Scripting.Tests.Dispatch;
using Orkeon.Cli.Commands.Scripting.Tests.Fixtures;
using Orkeon.Scripting;
using Orkeon.Scripting.Toolchain;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Cli.Commands.Scripting.Tests.Runtime;

/// <summary>
/// A scripted command's outcome as the read loop sees it: a handler that throws or rejects is a failed result
/// (<see cref="CommandResult.Failed"/>), which stops a replay; one that returns is not.
/// </summary>
public sealed class ScriptCommandOutcomeTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ScriptedTestConsole _console = new();

    public ScriptCommandOutcomeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "orkeon-script-outcome-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    private async Task<CommandResult> RunAsync(string handler)
    {
        var ct = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "probe.cmd.ts"), $$"""
            defineCommand({
              name: "probe",
              description: "Probe.",
              handler: {{handler}}
            });
            """, ct);
        var loader = new ScriptCommandLoader(
            fileSystem: new DiskBackedFileSystemService(_tempDir, virtualRoot: "/cmd"),
            transpiler: PassThroughTranspiler.Instance,
            engineFactory: new JsEngineFactory(),
            options: new ScriptCommandLoaderOptions { Directories = ImmutableArray.Create("/cmd") });
        var (registry, _) = await loader.LoadAndRegisterAsync(ct);
        var command = registry.ScriptCommands.Single(c => c.Name == "probe");
        return await command.ExecuteAsync(ChainHarness.Ctx("/probe", "x", _console), ct);
    }

    [Fact]
    public async Task ShouldFail_WhenTheHandlerThrows()
    {
        var result = await RunAsync("""function () { throw new Error("boom"); }""");

        Assert.True(result.Failed);
        Assert.StartsWith("Error: ", result.Message, StringComparison.Ordinal);
        Assert.Contains("boom", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldFail_WhenTheHandlerRejects()
    {
        var result = await RunAsync("""async function () { throw new Error("late"); }""");

        Assert.True(result.Failed);
        Assert.Contains("late", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldNotFail_WhenTheHandlerReturns()
    {
        var result = await RunAsync("function () { }");

        Assert.False(result.Failed);
    }
}
