using System.Text.Json;
using Orkeon.Hosting;
using Orkeon.Scripting.Cli.Commands;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// Coverage for <c>orkeon doctor</c> (WIN-03): exit codes, stable <c>--json</c> schema,
/// and the no-state-left-behind guarantee. The scratch cwd has no <c>Llm</c> section and
/// the global settings path is pinned to a nonexistent file, so every run stays offline.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class DoctorCommandTests : IDisposable
{
    /// <summary>Pinned check identifiers — the CI contract of <c>doctor --json</c>.</summary>
    private static readonly string[] ExpectedChecks =
    [
        "dotnet-runtime",
        "appsettings",
        "llm-config",
        "llm-reachability",
        "esbuild",
        "local-embeddings",
        "onnx-reranker",
        "tree-sitter",
        "workspace-write",
    ];

    private static readonly string[] ValidStatuses = ["ok", "warn", "fail"];

    public DoctorCommandTests()
    {
        // Pin the global per-user step onto a nonexistent path so a real
        // %APPDATA%/Orkeon/appsettings.json on the machine can never leak in.
        RunnerSettings.GlobalSettingsPathOverride =
            Path.Combine(Path.GetTempPath(), "orkeon-doctor-no-global-" + Guid.NewGuid().ToString("N"), "appsettings.json");
    }

    public void Dispose() =>
        RunnerSettings.GlobalSettingsPathOverride = AssemblyGlobalSettingsGuard.DefaultOverride;

    [Fact]
    public async Task HealthyDirectory_Exits0_AndPrintsEveryCheck()
    {
        using var scratch = new ScriptScratch();
        using var console = new TestConsole();

        var exit = await DoctorCommand.ExecuteAsync(new DoctorCommandOptions
        {
            WorkingDirectoryOverride = scratch.Root,
        });

        Assert.Equal(Program.ExitOk, exit);
        foreach (var check in ExpectedChecks)
            Assert.Contains(check, console.Stdout, StringComparison.Ordinal);
    }

    /// <summary>
    /// The reranker check reads the weights instead of asserting they exist.
    /// <para>
    /// It was a hard-coded <c>ok</c> with a hard-coded detail, on the reasoning that the
    /// weights are embedded resources of a package the CLI always references — true, and
    /// still not a check: a trimmed publish or a renamed resource leaves the reranker broken
    /// and the doctor cheerful. The measured size is the proof it looked.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TheOnnxRerankerCheck_ReadsTheWeights()
    {
        using var scratch = new ScriptScratch();
        using var console = new TestConsole();

        await DoctorCommand.ExecuteAsync(new DoctorCommandOptions
        {
            Json = true,
            WorkingDirectoryOverride = scratch.Root,
        });

        using var doc = JsonDocument.Parse(console.Stdout);
        var reranker = doc.RootElement.EnumerateArray()
            .Single(e => e.GetProperty("check").GetString() == "onnx-reranker");

        Assert.Equal("ok", reranker.GetProperty("status").GetString());
        Assert.Contains("MB", reranker.GetProperty("detail").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task JsonOutput_HasTheStableSchema()
    {
        using var scratch = new ScriptScratch();
        using var console = new TestConsole();

        var exit = await DoctorCommand.ExecuteAsync(new DoctorCommandOptions
        {
            Json = true,
            WorkingDirectoryOverride = scratch.Root,
        });

        Assert.Equal(Program.ExitOk, exit);
        using var doc = JsonDocument.Parse(console.Stdout);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);

        var checks = new List<string>();
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            checks.Add(element.GetProperty("check").GetString()!);
            Assert.Contains(element.GetProperty("status").GetString(), ValidStatuses);
            Assert.False(string.IsNullOrWhiteSpace(element.GetProperty("detail").GetString()));
        }
        Assert.Equal(ExpectedChecks, checks);
    }

    [Fact]
    public async Task NoLlmSection_ReportsWarnAndSkipsReachability_WithoutFailing()
    {
        using var scratch = new ScriptScratch();
        using var console = new TestConsole();

        var exit = await DoctorCommand.ExecuteAsync(new DoctorCommandOptions
        {
            Json = true,
            WorkingDirectoryOverride = scratch.Root,
        });

        Assert.Equal(Program.ExitOk, exit);
        using var doc = JsonDocument.Parse(console.Stdout);
        var byCheck = doc.RootElement.EnumerateArray()
            .ToDictionary(e => e.GetProperty("check").GetString()!, e => e);

        Assert.Equal("warn", byCheck["llm-config"].GetProperty("status").GetString());
        Assert.Contains("skipped", byCheck["llm-reachability"].GetProperty("detail").GetString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnwritableWorkspace_Exits1_AndMarksTheWriteCheckAsFail()
    {
        using var scratch = new ScriptScratch();
        // A FILE named .orkeon blocks the state directory — the only ❌-severity local check.
        scratch.WriteFile(".orkeon", "not a directory");
        using var console = new TestConsole();

        var exit = await DoctorCommand.ExecuteAsync(new DoctorCommandOptions
        {
            Json = true,
            WorkingDirectoryOverride = scratch.Root,
        });

        Assert.Equal(Program.ExitScriptError, exit);
        using var doc = JsonDocument.Parse(console.Stdout);
        var write = doc.RootElement.EnumerateArray()
            .Single(e => e.GetProperty("check").GetString() == "workspace-write");
        Assert.Equal("fail", write.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Doctor_LeavesNoStateBehind()
    {
        using var scratch = new ScriptScratch();
        var probeDir = Path.Combine(scratch.Root, "probe");
        Directory.CreateDirectory(probeDir);
        using var console = new TestConsole();

        var exit = await DoctorCommand.ExecuteAsync(new DoctorCommandOptions
        {
            WorkingDirectoryOverride = probeDir,
        });

        Assert.Equal(Program.ExitOk, exit);
        // The write probe cleaned up after itself: the cwd is exactly as it was.
        Assert.Empty(Directory.EnumerateFileSystemEntries(probeDir));
    }

    [Fact]
    public async Task ConfiguredLlm_IsReportedWithInferredProviderAndModel()
    {
        using var scratch = new ScriptScratch();
        scratch.WriteFile("appsettings.json",
            """{ "Llm": { "Model": "llama3.2", "BaseUrl": "http://localhost:11434" } }""");
        using var console = new TestConsole();

        var exit = await DoctorCommand.ExecuteAsync(new DoctorCommandOptions
        {
            Json = true,
            WorkingDirectoryOverride = scratch.Root,
        });

        using var doc = JsonDocument.Parse(console.Stdout);
        var byCheck = doc.RootElement.EnumerateArray()
            .ToDictionary(e => e.GetProperty("check").GetString()!, e => e);

        Assert.Equal("ok", byCheck["llm-config"].GetProperty("status").GetString());
        var detail = byCheck["llm-config"].GetProperty("detail").GetString()!;
        Assert.Contains("Ollama", detail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("llama3.2", detail, StringComparison.Ordinal);
        // Reachability ran (nothing listens on 11434 in CI → refusal is a fail; a local
        // Ollama would make it ok) — either way the check must not be "skipped".
        Assert.DoesNotContain("skipped", byCheck["llm-reachability"].GetProperty("detail").GetString(),
            StringComparison.OrdinalIgnoreCase);
        // The settings file was resolved next to the cwd.
        Assert.Equal("ok", byCheck["appsettings"].GetProperty("status").GetString());
    }
}
