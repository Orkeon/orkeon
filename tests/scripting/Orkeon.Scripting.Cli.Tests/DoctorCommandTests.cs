using Microsoft.Extensions.Configuration;
using System.Text.Json;
using Orkeon.Hosting;
using Orkeon.Scripting.Toolchain;
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
        "install-channel",
        "appsettings",
        "llm-config",
        "llm-profiles",
        "runner-settings",
        "llm-reachability",
        "esbuild",
        "tsc",
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

        var exit = await DoctorCommand.ExecuteAsync(console, new DoctorCommandOptions
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

        await DoctorCommand.ExecuteAsync(console, new DoctorCommandOptions
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

    /// <summary>
    /// The check names the channel the marker gives, the version that runs and the command
    /// that updates that channel — and never fails a run: it informs.
    /// </summary>
    [Fact]
    public async Task TheInstallChannelCheck_NamesTheChannelAndHowToUpdateIt()
    {
        using var scratch = new ScriptScratch();
        var executable = Path.Combine(scratch.Root, "install", "libexec", "orkeon");
        Directory.CreateDirectory(executable);
        scratch.WriteFile(Path.Combine("install", Orkeon.Constants.FileSystem.InstallChannels.MarkerFile), "zip\n");
        using var console = new TestConsole();

        var exit = await DoctorCommand.ExecuteAsync(console, new DoctorCommandOptions
        {
            Json = true,
            WorkingDirectoryOverride = scratch.Root,
            InstallDirectoryOverride = executable,
        });

        Assert.Equal(Program.ExitOk, exit);
        using var doc = JsonDocument.Parse(console.Stdout);
        var channel = doc.RootElement.EnumerateArray()
            .Single(e => e.GetProperty("check").GetString() == "install-channel");
        var detail = channel.GetProperty("detail").GetString()!;

        Assert.Equal("ok", channel.GetProperty("status").GetString());
        Assert.StartsWith("zip — ", detail, StringComparison.Ordinal);
        Assert.Contains("orkeon " + CliUsage.Version, detail, StringComparison.Ordinal);
        Assert.Contains("install.cmd", detail, StringComparison.Ordinal);
    }

    /// <summary>An installation older than the marker answers <c>unknown</c>, without an error.</summary>
    [Fact]
    public async Task TheInstallChannelCheck_WithoutAMarker_IsUnknownAndStillOk()
    {
        using var scratch = new ScriptScratch();
        using var console = new TestConsole();

        var exit = await DoctorCommand.ExecuteAsync(console, new DoctorCommandOptions
        {
            Json = true,
            WorkingDirectoryOverride = scratch.Root,
            InstallDirectoryOverride = scratch.OutDir,
        });

        Assert.Equal(Program.ExitOk, exit);
        using var doc = JsonDocument.Parse(console.Stdout);
        var channel = doc.RootElement.EnumerateArray()
            .Single(e => e.GetProperty("check").GetString() == "install-channel");

        Assert.Equal("ok", channel.GetProperty("status").GetString());
        Assert.StartsWith("unknown — ", channel.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    /// <summary>A marker that names nothing is worth a warning, never a failed run.</summary>
    [Fact]
    public async Task TheInstallChannelCheck_OnAMarkerThatNamesNothing_Warns_AndExits0()
    {
        using var scratch = new ScriptScratch();
        scratch.WriteFile(Path.Combine("out", Orkeon.Constants.FileSystem.InstallChannels.MarkerFile), "snap\n");
        using var console = new TestConsole();

        var exit = await DoctorCommand.ExecuteAsync(console, new DoctorCommandOptions
        {
            Json = true,
            WorkingDirectoryOverride = scratch.Root,
            InstallDirectoryOverride = scratch.OutDir,
        });

        Assert.Equal(Program.ExitOk, exit);
        using var doc = JsonDocument.Parse(console.Stdout);
        var channel = doc.RootElement.EnumerateArray()
            .Single(e => e.GetProperty("check").GetString() == "install-channel");

        Assert.Equal("warn", channel.GetProperty("status").GetString());
        Assert.Contains("'snap'", channel.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task JsonOutput_HasTheStableSchema()
    {
        using var scratch = new ScriptScratch();
        using var console = new TestConsole();

        var exit = await DoctorCommand.ExecuteAsync(console, new DoctorCommandOptions
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

        var exit = await DoctorCommand.ExecuteAsync(console, new DoctorCommandOptions
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
    public async Task ShouldDescribeModuleProvider_WhenDoctorRuns()
    {
        using var scratch = new ScriptScratch();
        scratch.WriteFile("appsettings.json", """
            { "RaggableTree": { "Enabled": false }, "Llm": { "Provider": "fake-cli", "Model": "opus" } }
            """);

        var (exit, lines) = await DoctorJsonAsync(scratch, modules: [new Orkeon.Tests.Shared.Doubles.FakeLlmProviderModule()]);

        Assert.Equal(Program.ExitOk, exit);
        var config = Assert.Single(lines, l => l.Check == "llm-config");
        Assert.Equal("ok", config.Status);
        Assert.Contains("fake-cli (provider module)", config.Detail, StringComparison.Ordinal);
        var reachability = Assert.Single(lines, l => l.Check == "llm-reachability");
        Assert.Contains("provider module fake-cli dials no address", reachability.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldFailLlmConfig_WhenProviderKeyHasNoModule()
    {
        using var scratch = new ScriptScratch();
        scratch.WriteFile("appsettings.json", """
            { "RaggableTree": { "Enabled": false }, "Llm": { "Provider": "other-cli" } }
            """);

        var (exit, lines) = await DoctorJsonAsync(scratch, modules: [new Orkeon.Tests.Shared.Doubles.FakeLlmProviderModule()]);

        Assert.Equal(Program.ExitScriptError, exit);
        var config = Assert.Single(lines, l => l.Check == "llm-config");
        Assert.Equal("fail", config.Status);
        Assert.Contains("'other-cli'", config.Detail, StringComparison.Ordinal);
        Assert.Contains("registered: fake-cli", config.Detail, StringComparison.Ordinal);
        // The run's start refuses the same key: the two checks agree.
        Assert.Contains(lines, l => l.Check == "runner-settings" && l.Status == "fail"
            && l.Detail.Contains("Llm:Provider: 'other-cli'", StringComparison.Ordinal));
    }

    /// <summary>ADR-020: the tool ships the agent-cli module, and its <c>Llm:AgentCli</c> settings pass the run's start.</summary>
    [Fact]
    public async Task ShouldDescribeAgentCli_WhenToolShipsItsModule()
    {
        using var scratch = new ScriptScratch();
        scratch.WriteFile("appsettings.json", """
            { "RaggableTree": { "Enabled": false }, "Llm": { "Provider": "agent-cli", "Model": "haiku", "AgentCli": { "MaxProcesses": 2 } } }
            """);

        var (exit, lines) = await DoctorJsonAsync(scratch);

        Assert.Equal(Program.ExitOk, exit);
        Assert.Contains("agent-cli (provider module)", Assert.Single(lines, l => l.Check == "llm-config").Detail, StringComparison.Ordinal);
        Assert.Equal("ok", Assert.Single(lines, l => l.Check == "runner-settings").Status);
    }

    [Fact]
    public async Task ShouldRefuseUnknownAgentCliKey_WhenSettingsMisspellIt()
    {
        using var scratch = new ScriptScratch();
        scratch.WriteFile("appsettings.json", """
            { "RaggableTree": { "Enabled": false }, "Llm": { "Provider": "agent-cli", "AgentCli": { "MaxProcess": 2 } } }
            """);

        var (exit, lines) = await DoctorJsonAsync(scratch);

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains(lines, l => l.Check == "runner-settings" && l.Status == "fail"
            && l.Detail.Contains("MaxProcess", StringComparison.Ordinal));
    }

    private static async Task<(int Exit, IReadOnlyList<(string Check, string Status, string Detail)> Lines)> DoctorJsonAsync(
        ScriptScratch scratch, Func<string>? tscResolver = null,
        IReadOnlyList<Orkeon.Application.Interfaces.Ports.ILlmProviderModule>? modules = null)
    {
        using var console = new TestConsole();
        var exit = await DoctorCommand.ExecuteAsync(console, new DoctorCommandOptions
        {
            Json = true,
            WorkingDirectoryOverride = scratch.Root,
            TscResolver = tscResolver,
            LlmModulesOverride = modules,
        });

        using var doc = JsonDocument.Parse(console.Stdout);
        return (exit, [.. doc.RootElement.EnumerateArray().Select(e => (
            e.GetProperty("check").GetString()!,
            e.GetProperty("status").GetString()!,
            e.GetProperty("detail").GetString()!))]);
    }

    /// <summary>
    /// GAP-40, decision 8 — <c>doctor</c> judges the file as <c>orkeon run</c> judges it at its start:
    /// one <c>fail</c> line per refusal, each naming its key, and exit 1. It judged the <c>Llm</c>
    /// section alone, and said all green on a file the run refused.
    /// </summary>
    [Fact]
    public async Task RunnerSettings_ReportsEveryRefusalOfTheRunsStart_OneLineEach()
    {
        using var scratch = new ScriptScratch();
        scratch.WriteFile("appsettings.json", """
            {
              "RaggableTree": { "Enabled": false },
              "Orkeon": {
                "Guardian": { "Enabled": "oui" },
                "Guardain": { "Enabled": true },
                "Rag": { "Rerank": { "Kind": "cohere" } }
              }
            }
            """);

        var (exit, lines) = await DoctorJsonAsync(scratch);

        Assert.Equal(Program.ExitScriptError, exit);
        var failures = lines.Where(l => l.Check == "runner-settings").ToList();
        Assert.Equal(3, failures.Count);
        Assert.All(failures, l => Assert.Equal("fail", l.Status));
        Assert.Contains(failures, l => l.Detail.Contains("Orkeon:Guardian:Enabled", StringComparison.Ordinal));
        Assert.Contains(failures, l => l.Detail.Contains("Orkeon:Guardain", StringComparison.Ordinal));
        Assert.Contains(failures, l => l.Detail.Contains("Orkeon:Rag:Rerank:Kind", StringComparison.Ordinal));
    }

    /// <summary>
    /// The file system's sections are judged as a run judges them, though <c>doctor</c> builds its
    /// host without a mount: a run always mounts, and its start refused what <c>doctor</c> passed.
    /// </summary>
    [Fact]
    public async Task RunnerSettings_JudgesTheSandboxSection_ThoughNothingIsMounted()
    {
        using var scratch = new ScriptScratch();
        scratch.WriteFile("appsettings.json", """
            { "RaggableTree": { "Enabled": false }, "Orkeon": { "Sandbox": { "CleanupOrphansOlderThan": "1 day" } } }
            """);

        var (exit, lines) = await DoctorJsonAsync(scratch);

        Assert.Equal(Program.ExitScriptError, exit);
        var line = Assert.Single(lines, l => l.Check == "runner-settings");
        Assert.Equal("fail", line.Status);
        Assert.Contains("Orkeon:Sandbox:CleanupOrphansOlderThan", line.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunnerSettings_ReportsADeclaredMountWhoseFolderIsMissing()
    {
        using var scratch = new ScriptScratch();
        var missing = Path.Combine(scratch.Root, "no-such-folder");
        scratch.WriteFile("appsettings.json", System.Text.Json.JsonSerializer.Serialize(new
        {
            RaggableTree = new { Enabled = false },
            Orkeon = new { FileSystem = new { Mounts = new[] { $"{missing}:/data:ro" } } },
        }));

        var (exit, lines) = await DoctorJsonAsync(scratch);

        Assert.Equal(Program.ExitScriptError, exit);
        var line = Assert.Single(lines, l => l.Check == "runner-settings");
        Assert.Equal("fail", line.Status);
        Assert.Contains("/data", line.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunnerSettings_IsOk_OnAFileTheRunAccepts_TheOnnxRerankerIncluded()
    {
        using var scratch = new ScriptScratch();
        scratch.WriteFile("appsettings.json", """
            { "RaggableTree": { "Enabled": false }, "Orkeon": { "Rag": { "Rerank": { "Kind": "onnx" } } } }
            """);

        var (_, lines) = await DoctorJsonAsync(scratch);

        var line = Assert.Single(lines, l => l.Check == "runner-settings");
        Assert.Equal("ok", line.Status);
        Assert.Contains("orkeon run", line.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// A section Orkeon knows and no shipped binary reads is not a refusal — a run starts on that
    /// file — and it is not nothing either: one <c>warn</c> line per section, the sentence the run
    /// writes on stderr.
    /// </summary>
    [Fact]
    public async Task RunnerSettings_WarnsOfEachSectionNoShippedBinaryReads_AndExits0()
    {
        using var scratch = new ScriptScratch();
        scratch.WriteFile("appsettings.json", """
            {
              "RaggableTree": { "Enabled": false },
              "ToolRateLimiting": { "GlobalToolRequestsPerMinute": 10 },
              "Orkeon": { "Dlp": { "Enabled": true }, "Host": { "Crews": [] } }
            }
            """);

        var (exit, lines) = await DoctorJsonAsync(scratch);

        Assert.Equal(Program.ExitOk, exit);
        var runner = lines.Where(l => l.Check == "runner-settings").ToList();
        Assert.Equal(2, runner.Count);
        Assert.All(runner, l => Assert.Equal("warn", l.Status));
        Assert.Contains(runner, l => l.Detail.StartsWith("ToolRateLimiting is read by no component of this host", StringComparison.Ordinal)
                                     && l.Detail.Contains("AddOrkeonToolRateLimiting()", StringComparison.Ordinal));
        Assert.Contains(runner, l => l.Detail.StartsWith("Orkeon:Dlp is read by no component of this host", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunnerSettings_ReportsARefusalAndANotice_OnTheSameFile()
    {
        using var scratch = new ScriptScratch();
        scratch.WriteFile("appsettings.json", """
            {
              "RaggableTree": { "Enabled": false },
              "TokenBudget": { "MaxTokensPerCrew": 1000 },
              "Orkeon": { "Guardian": { "Enabled": "oui" } }
            }
            """);

        var (exit, lines) = await DoctorJsonAsync(scratch);

        Assert.Equal(Program.ExitScriptError, exit);
        var runner = lines.Where(l => l.Check == "runner-settings").ToList();
        Assert.Equal(2, runner.Count);
        Assert.Contains(runner, l => l.Status == "fail" && l.Detail.Contains("Orkeon:Guardian:Enabled", StringComparison.Ordinal));
        Assert.Contains(runner, l => l.Status == "warn" && l.Detail.StartsWith("TokenBudget is read by no component of this host", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunnerSettings_IsSkipped_WhenTheLlmSectionIsAlreadyRefused()
    {
        using var scratch = new ScriptScratch();
        scratch.WriteFile("appsettings.json", """
            { "RaggableTree": { "Enabled": false }, "Llm": { "BaseUrl": "pas une url" } }
            """);

        var (exit, lines) = await DoctorJsonAsync(scratch);

        Assert.Equal(Program.ExitScriptError, exit);
        var failure = Assert.Single(lines, l => l.Status == "fail");
        Assert.Equal("llm-config", failure.Check);
        var runner = Assert.Single(lines, l => l.Check == "runner-settings");
        Assert.Contains("skipped", runner.Detail, StringComparison.Ordinal);
    }

    /// <summary>The swallowed default <c>Llm</c> value is the same refusal in <c>doctor</c> as in a run (decision 4).</summary>
    [Fact]
    public async Task LlmConfig_RefusesASwallowedDefaultValue_ByItsKey()
    {
        using var scratch = new ScriptScratch();
        scratch.WriteFile("appsettings.json", """
            { "RaggableTree": { "Enabled": false }, "Llm": { "BaseUrl": "http://localhost:11434", "TimeoutSeconds": "600s" } }
            """);

        var (_, lines) = await DoctorJsonAsync(scratch);

        var config = Assert.Single(lines, l => l.Check == "llm-config");
        Assert.Equal("fail", config.Status);
        Assert.Contains("Llm:TimeoutSeconds", config.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>doctor</c> and <c>orkeon run</c> cannot contradict each other on one file: the refusal the
    /// run prints as its last line is the line <c>doctor</c> reports.
    /// </summary>
    [Fact]
    public async Task RunnerSettings_SaysWhatTheRunSays_OnTheSameFile()
    {
        using var scratch = new ScriptScratch();
        var settings = scratch.WriteFile("appsettings.json", """
            { "RaggableTree": { "Enabled": false }, "Orkeon": { "Guardian": { "Enabled": "oui" } } }
            """);
        var crew = scratch.WriteScript("crew.yaml", "name: c\ngoal: g\n");

        var (_, lines) = await DoctorJsonAsync(scratch);
        string runLine;
        using (var console = new TestConsole())
        {
            var exit = await RunCommand.ExecuteAsync(console, new RunCommandOptions
            {
                ScriptPath = crew,
                SettingsPath = settings,
                Validate = true,
                AllowExternalMounts = true,
            });
            Assert.Equal(Program.ExitScriptError, exit);
            runLine = console.Stderr.TrimEnd().Split('\n')[^1].TrimEnd('\r');
        }

        var doctorLine = Assert.Single(lines, l => l.Check == "runner-settings");
        Assert.Equal($"ERROR: {doctorLine.Detail}", runLine);
    }

    [Fact]
    public async Task UnwritableWorkspace_Exits1_AndMarksTheWriteCheckAsFail()
    {
        using var scratch = new ScriptScratch();
        // A FILE named .orkeon blocks the state directory — the only ❌-severity local check.
        scratch.WriteFile(".orkeon", "not a directory");
        using var console = new TestConsole();

        var exit = await DoctorCommand.ExecuteAsync(console, new DoctorCommandOptions
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

        var exit = await DoctorCommand.ExecuteAsync(console, new DoctorCommandOptions
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

        var exit = await DoctorCommand.ExecuteAsync(console, new DoctorCommandOptions
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

    // ── Where each key comes from, never the key ─────────────────

    private static Dictionary<string, JsonElement> ByCheck(JsonDocument doc) =>
        doc.RootElement.EnumerateArray()
            .GroupBy(e => e.GetProperty("check").GetString()!)
            .ToDictionary(g => g.Key, g => g.First());

    private static List<JsonElement> Rows(JsonDocument doc, string check) =>
        [.. doc.RootElement.EnumerateArray().Where(e => e.GetProperty("check").GetString() == check)];

    [Fact]
    public async Task LlmConfig_SaysWhereTheDefaultKeyComesFrom_NeverTheKeyNorTheVariable()
    {
        var variable = "ORKEON_TEST_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(variable, "sk-doctor-0123456789");
        try
        {
            using var scratch = new ScriptScratch();
            scratch.WriteFile("appsettings.json",
                $$"""{ "Llm": { "Model": "llama3.2", "BaseUrl": "http://localhost:11434", "ApiKeyEnvVar": "{{variable}}" } }""");
            using var console = new TestConsole();

            await DoctorCommand.ExecuteAsync(console, new DoctorCommandOptions { Json = true, WorkingDirectoryOverride = scratch.Root });

            using var doc = JsonDocument.Parse(console.Stdout);
            var detail = ByCheck(doc)["llm-config"].GetProperty("detail").GetString()!;
            Assert.Contains("API key from the variable named by Llm:ApiKeyEnvVar (process environment)", detail, StringComparison.Ordinal);
            Assert.DoesNotContain("sk-doctor", console.Stdout, StringComparison.Ordinal);
            Assert.DoesNotContain(variable, console.Stdout, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public async Task LlmReachability_QueriesTheCatalogueWithTheResolvedKey()
    {
        var variable = "ORKEON_TEST_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(variable, "sk-resolved-0123456789");
        using var server = new CatalogueServer();
        try
        {
            using var scratch = new ScriptScratch();
            scratch.WriteFile("appsettings.json", $$"""
                { "Llm": { "Model": "test-model", "BaseUrl": "{{server.BaseUrl}}", "ApiKeyEnvVar": "{{variable}}" } }
                """);
            using var console = new TestConsole();

            await DoctorCommand.ExecuteAsync(console, new DoctorCommandOptions { Json = true, WorkingDirectoryOverride = scratch.Root });

            using var doc = JsonDocument.Parse(console.Stdout);
            Assert.Equal("ok", ByCheck(doc)["llm-reachability"].GetProperty("status").GetString());
            Assert.Equal("Bearer sk-resolved-0123456789", Assert.Single(server.Authorizations));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public async Task AnUnresolvedDefaultReference_IsAWarningOfLlmConfig()
    {
        var variable = "ORKEON_TEST_" + Guid.NewGuid().ToString("N");
        using var scratch = new ScriptScratch();
        scratch.WriteFile("appsettings.json",
            $$"""{ "Llm": { "Model": "llama3.2", "BaseUrl": "http://localhost:11434", "ApiKeyEnvVar": "{{variable}}" } }""");
        using var console = new TestConsole();

        await DoctorCommand.ExecuteAsync(console, new DoctorCommandOptions { Json = true, WorkingDirectoryOverride = scratch.Root });

        using var doc = JsonDocument.Parse(console.Stdout);
        var config = ByCheck(doc)["llm-config"];
        Assert.Equal("warn", config.GetProperty("status").GetString());
        Assert.Contains("Llm:ApiKeyEnvVar", config.GetProperty("detail").GetString()!, StringComparison.Ordinal);
        Assert.Contains("not set", config.GetProperty("detail").GetString()!, StringComparison.Ordinal);
        Assert.DoesNotContain(variable, console.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OneLineCoversTheProfiles_AndEachUnresolvedReferenceIsAWarningLine()
    {
        var unset = "ORKEON_TEST_" + Guid.NewGuid().ToString("N");
        using var scratch = new ScriptScratch();
        // No default: the run would answer on the echo provider, and reachability is skipped —
        // this test is about the profiles' lines.
        scratch.WriteFile("appsettings.json", $$"""
            {
              "Llm": {
                "Profiles": {
                  "z-ai": { "BaseUrl": "https://api.z.ai/api/paas/v4", "Model": "glm-5", "ApiKeyEnvVar": "{{unset}}" },
                  "kimi": { "BaseUrl": "https://api.moonshot.ai/v1", "Model": "kimi-k3", "ApiKeyEnvVar": "{{unset}}" },
                  "inline": { "BaseUrl": "https://api.anthropic.com/v1", "ApiKey": "sk-inline-doctor" },
                  "local": { "BaseUrl": "http://localhost:11434", "Model": "qwen3" }
                }
              }
            }
            """);
        using var console = new TestConsole();

        var exit = await DoctorCommand.ExecuteAsync(console, new DoctorCommandOptions { Json = true, WorkingDirectoryOverride = scratch.Root });

        Assert.Equal(Program.ExitOk, exit);
        using var doc = JsonDocument.Parse(console.Stdout);
        var profiles = Assert.Single(Rows(doc, "llm-profiles"));
        var summary = profiles.GetProperty("detail").GetString()!;
        foreach (var id in new[] { "z-ai", "kimi", "inline", "local" })
            Assert.Contains(id, summary, StringComparison.Ordinal);
        Assert.Contains("from configuration (Llm:Profiles:inline:ApiKey)", summary, StringComparison.Ordinal);

        var warnings = Rows(doc, "llm-profile-key");
        Assert.Equal(2, warnings.Count);
        Assert.All(warnings, w => Assert.Equal("warn", w.GetProperty("status").GetString()));
        Assert.Contains(warnings, w => w.GetProperty("detail").GetString()!.Contains("Llm:Profiles:z-ai:ApiKeyEnvVar", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.GetProperty("detail").GetString()!.Contains("Llm:Profiles:kimi:ApiKeyEnvVar", StringComparison.Ordinal));
        Assert.DoesNotContain(unset, console.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-inline-doctor", console.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnApiKeyWrittenAsAPlaceholder_FailsLlmConfigWithTheFix()
    {
        using var scratch = new ScriptScratch();
        scratch.WriteFile("appsettings.json",
            """{ "Llm": { "Model": "deepseek-chat", "BaseUrl": "https://api.deepseek.com", "ApiKey": "${DEEPSEEK_API_KEY}" } }""");
        using var console = new TestConsole();

        var exit = await DoctorCommand.ExecuteAsync(console, new DoctorCommandOptions { Json = true, WorkingDirectoryOverride = scratch.Root });

        Assert.Equal(Program.ExitScriptError, exit);
        using var doc = JsonDocument.Parse(console.Stdout);
        var config = ByCheck(doc)["llm-config"];
        Assert.Equal("fail", config.GetProperty("status").GetString());
        Assert.Contains("\"ApiKeyEnvVar\": \"DEEPSEEK_API_KEY\"", config.GetProperty("detail").GetString()!, StringComparison.Ordinal);
        Assert.Contains("skipped", ByCheck(doc)["llm-reachability"].GetProperty("detail").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    // ── GAP-50-55 F6: the provider doctor names is the one the run builds ───

    [Fact]
    [Trait("BR", "CH-08")]
    public async Task ShouldSayOpenAICompatible_WhenTheAddressIsALocalServerOtherThanOllama()
    {
        using var scratch = new ScriptScratch();
        scratch.WriteFile("appsettings.json", """{ "Llm": { "BaseUrl": "http://localhost:8080/v1" } }""");

        var (_, lines) = await DoctorJsonAsync(scratch);

        var config = Assert.Single(lines, l => l.Check == "llm-config");
        Assert.Contains("OpenAI-compatible (custom)", config.Detail, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-08")]
    public async Task ShouldSayGrok_WhenOnlyAnXaiKeyIsSet()
    {
        var value = "xai-" + Guid.NewGuid().ToString("N");
        using var scratch = new ScriptScratch();
        scratch.WriteFile("appsettings.json", $$"""{ "Llm": { "ApiKey": "{{value}}" } }""");

        var (_, lines) = await DoctorJsonAsync(scratch);

        var config = Assert.Single(lines, l => l.Check == "llm-config");
        Assert.Contains("Grok", config.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(value, config.Detail, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-09")]
    public async Task ShouldWarn_WhenTheAddressHasNoPath()
    {
        using var scratch = new ScriptScratch();
        scratch.WriteFile("appsettings.json", """
            {
              "Llm": {
                "BaseUrl": "http://localhost:8080",
                "Model": "m",
                "Profiles": { "local": { "BaseUrl": "http://localhost:8080", "Model": "m" } }
              }
            }
            """);

        var (_, lines) = await DoctorJsonAsync(scratch);

        var config = Assert.Single(lines, l => l.Check == "llm-config");
        Assert.Equal("warn", config.Status);
        Assert.Contains(Orkeon.Constants.Configuration.OperatorMessages.LlmBaseUrlSilent("Llm:BaseUrl", "http://localhost:8080"),
            config.Detail, StringComparison.Ordinal);
        var warn = Assert.Single(lines, l => l.Check == "llm-profiles" && l.Status == "warn");
        Assert.Contains(Orkeon.Constants.Configuration.OperatorMessages.LlmBaseUrlSilent("Llm:Profiles:local:BaseUrl", "http://localhost:8080"),
            warn.Detail, StringComparison.Ordinal);
    }

    // ── CH-24: a missing tsc is judged by the type-check mode ───────────────

    private static IConfiguration TscConfig(string? typeCheck, string? tscPath = null)
    {
        var values = new Dictionary<string, string?>();
        if (typeCheck is not null)
            values["Orkeon:Scripting:Toolchain:TypeCheck"] = typeCheck;
        if (tscPath is not null)
            values["Orkeon:Scripting:Toolchain:TscPath"] = tscPath;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static string MissingTscPath() =>
        Path.Combine(Path.GetTempPath(), "orkeon-doctor-no-tsc-" + Guid.NewGuid().ToString("N"), "tsc");

    private static Func<string> TscMissing() =>
        () => throw new TscNotFoundException("TypeScript compiler (tsc) not found.");

    [Fact]
    [Trait("BR", "CH-24")]
    public async Task ShouldReportTscOkAndOptional_WhenItIsMissingUnderValidate()
    {
        var result = await DoctorCommand.CheckTscAsync(TscConfig(null), TscMissing(), CancellationToken.None);

        Assert.Equal("ok", result.Status);
        Assert.Contains("optional", result.Detail, StringComparison.Ordinal);
        Assert.Contains("orkeon run --validate", result.Detail, StringComparison.Ordinal);
        Assert.Contains("npm install -g typescript", result.Detail, StringComparison.Ordinal);
        Assert.Contains("Orkeon:Scripting:Toolchain:TscPath", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-24")]
    public async Task ShouldReportTscOk_WhenItIsMissingAndTheTypeCheckIsOff()
    {
        var result = await DoctorCommand.CheckTscAsync(TscConfig("Off"), TscMissing(), CancellationToken.None);

        Assert.Equal("ok", result.Status);
        Assert.Equal("not installed — the type check of .ork.ts scripts is off", result.Detail);
    }

    [Fact]
    [Trait("BR", "CH-24")]
    public async Task ShouldReportTscNotOk_WithTheInstalledRemedyFirst_WhenItIsMissingUnderAlways()
    {
        var result = await DoctorCommand.CheckTscAsync(TscConfig("Always"), TscMissing(), CancellationToken.None);

        Assert.Equal("fail", result.Status);
        var install = result.Detail.IndexOf("npm install -g typescript", StringComparison.Ordinal);
        Assert.True(install >= 0, result.Detail);
        var checkout = result.Detail.IndexOf("tools/scripting-typecheck", StringComparison.Ordinal);
        Assert.True(checkout < 0 || checkout > install, result.Detail);
        Assert.Contains("Orkeon:Scripting:Toolchain:TscPath", result.Detail, StringComparison.Ordinal);
        Assert.Contains("TypeCheck: off", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-24")]
    [Trait("BR", "CH-32")]
    public async Task ShouldReportNoWarning_WhenTheDoctorRunsWithoutTscUnderDefaults()
    {
        using var scratch = new ScriptScratch();

        var (_, lines) = await DoctorJsonAsync(scratch, TscMissing());

        var tsc = Assert.Single(lines, l => l.Check == "tsc");
        Assert.Equal("ok", tsc.Status);
        Assert.Contains("optional", tsc.Detail, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-32")]
    [Trait("BR", "CH-40")]
    public async Task ShouldReportTscOk_WhenTheConfiguredTscPathDoesNotExistUnderOff()
    {
        Func<string> missing = () => throw new TscNotFoundException(
            "TypeScript compiler not found: 'Orkeon:Scripting:Toolchain:TscPath' names a file that does not exist.");
        var path = MissingTscPath();
        var result = await DoctorCommand.CheckTscAsync(TscConfig("Off", path), missing, CancellationToken.None);

        Assert.Equal("ok", result.Status);
        Assert.Contains($"TscPath {path} does not exist; unused while the type check is off", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-24")]
    public async Task ShouldKeepTscWarn_WhenTheConfiguredTscPathDoesNotExistUnderValidate()
    {
        Func<string> missing = () => throw new TscNotFoundException(
            "TypeScript compiler not found: 'Orkeon:Scripting:Toolchain:TscPath' names a file that does not exist.");
        var result = await DoctorCommand.CheckTscAsync(TscConfig("Validate", MissingTscPath()), missing, CancellationToken.None);

        Assert.Equal("warn", result.Status);
        Assert.Equal("not found — TypeScript compiler not found: 'Orkeon:Scripting:Toolchain:TscPath' names a file that does not exist.", result.Detail);
    }

    [Fact]
    [Trait("BR", "CH-24")]
    public async Task ShouldReportTscNotOk_WhenTheConfiguredTscPathDoesNotExistUnderAlways()
    {
        var result = await DoctorCommand.CheckTscAsync(TscConfig("Always", MissingTscPath()), TscMissing(), CancellationToken.None);

        Assert.Equal("fail", result.Status);
    }

    [Fact]
    [Trait("BR", "CH-40")]
    public async Task ShouldPutTheInstalledRemedyFirst_WhenTheConfiguredTscPathDoesNotExistUnderAlways()
    {
        Func<string> missing = () => throw new TscNotFoundException(
            "TypeScript compiler not found: 'Orkeon:Scripting:Toolchain:TscPath' names a file that does not exist.");
        var result = await DoctorCommand.CheckTscAsync(TscConfig("Always", MissingTscPath()), missing, CancellationToken.None);

        Assert.Equal("fail", result.Status);
        var reason = result.Detail.IndexOf("names a file that does not exist", StringComparison.Ordinal);
        var install = result.Detail.IndexOf("npm install -g typescript", StringComparison.Ordinal);
        var checkout = result.Detail.IndexOf("tools/scripting-typecheck", StringComparison.Ordinal);
        Assert.True(reason >= 0, result.Detail);
        Assert.True(install > reason, result.Detail);
        Assert.True(checkout < 0 || checkout > install, result.Detail);
    }

    [Theory]
    [Trait("BR", "CH-40")]
    [InlineData("Off")]
    [InlineData("Validate")]
    [InlineData("Always")]
    public async Task ShouldReportTscOkOnlyUnderOff_WhenTscDoesNotAnswerItsVersion(string mode)
    {
        // Seam: the TscResolver-style Func<string> of CheckTscAsync, returning a path that cannot run.
        var binary = MissingTscPath();
        var result = await DoctorCommand.CheckTscAsync(TscConfig(mode), () => binary, CancellationToken.None);

        if (mode == "Off")
            Assert.Equal("ok", result.Status);
        else
            Assert.Equal("warn", result.Status);
        Assert.Contains(binary, result.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("BR", "CH-40")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ShouldListTheCheckoutBootstrap_OnlyWhenRunFromACheckout(bool fromCheckout)
    {
        using var scratch = new ScriptScratch();
        scratch.WriteFile("appsettings.json", """{ "Orkeon": { "Scripting": { "Toolchain": { "TypeCheck": "Always" } } } }""");
        var install = Path.Combine(scratch.Root, "install", "bin");
        Directory.CreateDirectory(install);
        if (fromCheckout)
            Directory.CreateDirectory(Path.Combine(scratch.Root, "install", "tools", "scripting-typecheck"));
        using var console = new TestConsole();

        await DoctorCommand.ExecuteAsync(console, new DoctorCommandOptions
        {
            Json = true,
            WorkingDirectoryOverride = scratch.Root,
            InstallDirectoryOverride = install,
            TscResolver = TscMissing(),
        });

        using var doc = JsonDocument.Parse(console.Stdout);
        var tsc = doc.RootElement.EnumerateArray().Single(e => e.GetProperty("check").GetString() == "tsc");
        var detail = tsc.GetProperty("detail").GetString()!;
        Assert.Equal("fail", tsc.GetProperty("status").GetString());
        Assert.Equal(fromCheckout, detail.Contains("npm --prefix tools/scripting-typecheck install --prefer-offline", StringComparison.Ordinal));
    }
}
