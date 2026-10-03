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
        "llm-profiles",
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

    // ── STUDIO-49: where each key comes from, never the key ─────────────────

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

            await DoctorCommand.ExecuteAsync(new DoctorCommandOptions { Json = true, WorkingDirectoryOverride = scratch.Root });

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
                { "Llm": { "Model": "test-model", "BaseUrl": "http://127.0.0.1:{{server.Port}}/v1", "ApiKeyEnvVar": "{{variable}}" } }
                """);
            using var console = new TestConsole();

            await DoctorCommand.ExecuteAsync(new DoctorCommandOptions { Json = true, WorkingDirectoryOverride = scratch.Root });

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

        await DoctorCommand.ExecuteAsync(new DoctorCommandOptions { Json = true, WorkingDirectoryOverride = scratch.Root });

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

        var exit = await DoctorCommand.ExecuteAsync(new DoctorCommandOptions { Json = true, WorkingDirectoryOverride = scratch.Root });

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

        var exit = await DoctorCommand.ExecuteAsync(new DoctorCommandOptions { Json = true, WorkingDirectoryOverride = scratch.Root });

        Assert.Equal(Program.ExitScriptError, exit);
        using var doc = JsonDocument.Parse(console.Stdout);
        var config = ByCheck(doc)["llm-config"];
        Assert.Equal("fail", config.GetProperty("status").GetString());
        Assert.Contains("\"ApiKeyEnvVar\": \"DEEPSEEK_API_KEY\"", config.GetProperty("detail").GetString()!, StringComparison.Ordinal);
        Assert.Contains("skipped", ByCheck(doc)["llm-reachability"].GetProperty("detail").GetString()!, StringComparison.OrdinalIgnoreCase);
    }
}
