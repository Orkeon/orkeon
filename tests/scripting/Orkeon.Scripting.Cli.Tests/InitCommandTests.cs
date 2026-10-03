using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Hosting;
using Orkeon.Infrastructure.Constants.Llm;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Scripting.Cli.Commands;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// Coverage for <c>orkeon init</c> (WIN-02): non-interactive generation per provider,
/// overwrite protection, global-path default, provider-detection pinning, and the
/// no-secret-leaked guarantee. Everything runs with <c>--no-probe</c> so the suite
/// stays offline.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class InitCommandTests
{
    // --- non-interactive generation per provider ---

    [Fact]
    public async Task Ollama_WritesValidJson_WithLocalBaseUrl_AndNoApiKey()
    {
        using var scratch = new ScriptScratch();
        var target = Path.Combine(scratch.Root, "appsettings.json");
        using var console = new TestConsole();

        var exit = await InitCommand.ExecuteAsync(new InitCommandOptions
        {
            Provider = "ollama",
            OutputPath = target,
            NoProbe = true,
        });

        Assert.Equal(Program.ExitOk, exit);
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
        var llm = doc.RootElement.GetProperty("Llm");
        Assert.Equal("http://localhost:11434", llm.GetProperty("BaseUrl").GetString());
        Assert.Equal(ProviderDefaults.ForProvider("ollama"), llm.GetProperty("Model").GetString());
        Assert.False(llm.TryGetProperty("ApiKey", out _));
    }

    [Fact]
    public async Task DockerModelRunner_WritesTemplateDefaults()
    {
        using var scratch = new ScriptScratch();
        var target = Path.Combine(scratch.Root, "appsettings.json");
        using var console = new TestConsole();

        var exit = await InitCommand.ExecuteAsync(new InitCommandOptions
        {
            Provider = "docker-model-runner",
            OutputPath = target,
            NoProbe = true,
        });

        Assert.Equal(Program.ExitOk, exit);
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
        var llm = doc.RootElement.GetProperty("Llm");
        Assert.Equal("http://localhost:12434/engines/llama.cpp/v1", llm.GetProperty("BaseUrl").GetString());
        Assert.Equal("ai/granite-4.0-h-tiny", llm.GetProperty("Model").GetString());
        Assert.Equal("not-needed", llm.GetProperty("ApiKey").GetString());
    }

    [Fact]
    public async Task OpenAI_DefaultsToEnvVarReference_AndTellsTheUser()
    {
        using var scratch = new ScriptScratch();
        var target = Path.Combine(scratch.Root, "appsettings.json");
        using var console = new TestConsole();

        var exit = await InitCommand.ExecuteAsync(new InitCommandOptions
        {
            Provider = "openai",
            OutputPath = target,
            NoProbe = true,
        });

        Assert.Equal(Program.ExitOk, exit);
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
        var llm = doc.RootElement.GetProperty("Llm");
        Assert.Equal("https://api.openai.com/v1", llm.GetProperty("BaseUrl").GetString());
        // The key is a reference to an environment variable, never written to the file.
        Assert.False(llm.TryGetProperty("ApiKey", out _));
        Assert.Contains("ORKEON_Llm__ApiKey", console.Stdout + console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InlineApiKey_IsWrittenToTheFile_WarnedAbout_AndNeverEchoed()
    {
        using var scratch = new ScriptScratch();
        var target = Path.Combine(scratch.Root, "appsettings.json");
        const string secret = "sk-test-secret-do-not-log-1234567890";
        using var console = new TestConsole();

        var exit = await InitCommand.ExecuteAsync(new InitCommandOptions
        {
            Provider = "openai",
            ApiKey = secret,
            OutputPath = target,
            NoProbe = true,
        });

        Assert.Equal(Program.ExitOk, exit);
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
        Assert.Equal(secret, doc.RootElement.GetProperty("Llm").GetProperty("ApiKey").GetString());
        // Plain-text storage is warned about, and the key itself never reaches the console.
        Assert.Contains("plain text", console.Stderr, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(secret, console.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Custom_RequiresBaseUrlAndModel()
    {
        using var scratch = new ScriptScratch();
        var target = Path.Combine(scratch.Root, "appsettings.json");
        using var console = new TestConsole();

        var exit = await InitCommand.ExecuteAsync(new InitCommandOptions
        {
            Provider = "custom",
            OutputPath = target,
            NoProbe = true,
        });

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.False(File.Exists(target));
        Assert.Contains("--base-url", console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Custom_WritesTheSuppliedEndpoint()
    {
        using var scratch = new ScriptScratch();
        var target = Path.Combine(scratch.Root, "appsettings.json");
        using var console = new TestConsole();

        var exit = await InitCommand.ExecuteAsync(new InitCommandOptions
        {
            Provider = "custom",
            BaseUrl = "https://api.deepseek.com",
            Model = "deepseek-v4-flash",
            OutputPath = target,
            NoProbe = true,
        });

        Assert.Equal(Program.ExitOk, exit);
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
        var llm = doc.RootElement.GetProperty("Llm");
        Assert.Equal("https://api.deepseek.com", llm.GetProperty("BaseUrl").GetString());
        Assert.Equal("deepseek-v4-flash", llm.GetProperty("Model").GetString());
    }

    [Fact]
    public async Task None_WritesFileWithoutLlmSection_AndExplainsTheEchoFallback()
    {
        using var scratch = new ScriptScratch();
        var target = Path.Combine(scratch.Root, "appsettings.json");
        using var console = new TestConsole();

        var exit = await InitCommand.ExecuteAsync(new InitCommandOptions
        {
            Provider = "none",
            OutputPath = target,
            NoProbe = true,
        });

        Assert.Equal(Program.ExitOk, exit);
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
        Assert.False(doc.RootElement.TryGetProperty("Llm", out _));
        Assert.Contains("undefined-llm", console.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownProvider_Fails()
    {
        using var scratch = new ScriptScratch();
        using var console = new TestConsole();

        var exit = await InitCommand.ExecuteAsync(new InitCommandOptions
        {
            Provider = "skynet",
            OutputPath = Path.Combine(scratch.Root, "appsettings.json"),
            NoProbe = true,
        });

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("skynet", console.Stderr, StringComparison.Ordinal);
    }

    // --- overwrite protection ---

    [Fact]
    public async Task ExistingFile_IsNotOverwritten_WithoutForce()
    {
        using var scratch = new ScriptScratch();
        var target = scratch.WriteFile("appsettings.json", """{ "keep": true }""");
        using var console = new TestConsole();

        var exit = await InitCommand.ExecuteAsync(new InitCommandOptions
        {
            Provider = "ollama",
            OutputPath = target,
            NoProbe = true,
        });

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("--force", console.Stderr, StringComparison.Ordinal);
        Assert.Equal("""{ "keep": true }""", await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExistingFile_IsOverwritten_WithForce()
    {
        using var scratch = new ScriptScratch();
        var target = scratch.WriteFile("appsettings.json", """{ "keep": true }""");
        using var console = new TestConsole();

        var exit = await InitCommand.ExecuteAsync(new InitCommandOptions
        {
            Provider = "ollama",
            OutputPath = target,
            Force = true,
            NoProbe = true,
        });

        Assert.Equal(Program.ExitOk, exit);
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
        Assert.True(doc.RootElement.TryGetProperty("Llm", out _));
    }

    // --- global path default + resolution from any cwd ---

    [Fact]
    public async Task DefaultTarget_IsTheGlobalPath_AndIsResolvedFromAnyCwd()
    {
        using var scratch = new ScriptScratch();
        var globalPath = Path.Combine(scratch.Root, "config", "Orkeon", "appsettings.json");
        RunnerSettings.GlobalSettingsPathOverride = globalPath;
        try
        {
            using var console = new TestConsole();
            var exit = await InitCommand.ExecuteAsync(new InitCommandOptions
            {
                Provider = "docker-model-runner",
                NoProbe = true,
            });

            Assert.Equal(Program.ExitOk, exit);
            Assert.True(File.Exists(globalPath));
            Assert.Contains(globalPath, console.Stdout, StringComparison.Ordinal);

            // The written file is found by the runner resolution chain from ANY directory.
            var elsewhere = Path.Combine(scratch.Root, "some", "other", "cwd");
            Directory.CreateDirectory(elsewhere);
            var resolved = RunnerSettings.ResolveSettingsPath(null, elsewhere, quiet: true);
            Assert.Equal(globalPath, resolved);
        }
        finally
        {
            RunnerSettings.GlobalSettingsPathOverride = AssemblyGlobalSettingsGuard.DefaultOverride;
        }
    }

    // --- provider-detection pinning (WIN-02: every generated BaseUrl must be
    //     recognised by LlmProviderFactory's BaseUrl inference) ---

    [Theory]
    [InlineData("ollama", typeof(OllamaLlmProvider))]
    [InlineData("docker-model-runner", typeof(OpenAIProvider))]
    [InlineData("openai", typeof(OpenAIProvider))]
    public async Task GeneratedBaseUrl_IsRecognisedByProviderDetection(string provider, Type expectedProvider)
    {
        using var scratch = new ScriptScratch();
        var target = Path.Combine(scratch.Root, "appsettings.json");
        using var console = new TestConsole();

        var exit = await InitCommand.ExecuteAsync(new InitCommandOptions
        {
            Provider = provider,
            OutputPath = target,
            NoProbe = true,
        });
        Assert.Equal(Program.ExitOk, exit);

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
        var llm = doc.RootElement.GetProperty("Llm");
        var config = LlmConfig.Create(llm.GetProperty("Model").GetString()!) with
        {
            BaseUrl = new Uri(llm.GetProperty("BaseUrl").GetString()!),
#pragma warning disable CS0618 // pinning test drives the factory directly, no secret store
            ApiKey = "test-key",
#pragma warning restore CS0618
        };

        using var httpFactory = new CliHttpClientFactory();
        var factory = new LlmProviderFactory(httpFactory, NullLoggerFactory.Instance);
        var created = factory.Create(config);

        var adapter = Assert.IsType<LlmProviderAdapter>(created);
        Assert.IsType(expectedProvider, adapter.UnderlyingProvider);
    }

    // --- interactive wizard ---

    [Fact]
    public async Task Wizard_OfflineChoice_WritesFileWithoutLlmSection()
    {
        using var scratch = new ScriptScratch();
        var target = Path.Combine(scratch.Root, "appsettings.json");
        using var console = new TestConsole();
        using var input = new StringReader("5\n");

        var exit = await InitCommand.ExecuteAsync(new InitCommandOptions
        {
            OutputPath = target,
            NoProbe = true,
            InputOverride = input,
        });

        Assert.Equal(Program.ExitOk, exit);
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
        Assert.False(doc.RootElement.TryGetProperty("Llm", out _));
        // The wizard listed all five provider choices.
        Assert.Contains("Ollama", console.Stdout, StringComparison.Ordinal);
        Assert.Contains("Docker Model Runner", console.Stdout, StringComparison.Ordinal);
        Assert.Contains("OpenAI", console.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Wizard_OllamaChoice_AcceptsDefaults_AndWritesNoApiKey()
    {
        using var scratch = new ScriptScratch();
        var target = Path.Combine(scratch.Root, "appsettings.json");
        using var console = new TestConsole();
        // 1 = Ollama, then empty line = accept the default model.
        using var input = new StringReader("1\n\n");

        var exit = await InitCommand.ExecuteAsync(new InitCommandOptions
        {
            OutputPath = target,
            NoProbe = true,
            InputOverride = input,
        });

        Assert.Equal(Program.ExitOk, exit);
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
        var llm = doc.RootElement.GetProperty("Llm");
        Assert.Equal("http://localhost:11434", llm.GetProperty("BaseUrl").GetString());
        Assert.Equal(ProviderDefaults.ForProvider("ollama"), llm.GetProperty("Model").GetString());
        Assert.False(llm.TryGetProperty("ApiKey", out _));
        // The Ollama branch surfaces the model-listing hint.
        Assert.Contains("orkeon llm models", console.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Wizard_DockerModelRunnerChoice_AcceptsCustomModel()
    {
        using var scratch = new ScriptScratch();
        var target = Path.Combine(scratch.Root, "appsettings.json");
        using var console = new TestConsole();
        // 2 = Docker Model Runner, then an explicit model overriding the default.
        using var input = new StringReader("2\nai/smollm2\n");

        var exit = await InitCommand.ExecuteAsync(new InitCommandOptions
        {
            OutputPath = target,
            NoProbe = true,
            InputOverride = input,
        });

        Assert.Equal(Program.ExitOk, exit);
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
        var llm = doc.RootElement.GetProperty("Llm");
        Assert.Equal("http://localhost:12434/engines/llama.cpp/v1", llm.GetProperty("BaseUrl").GetString());
        Assert.Equal("ai/smollm2", llm.GetProperty("Model").GetString());
        Assert.Equal("not-needed", llm.GetProperty("ApiKey").GetString());
    }

    [Fact]
    public async Task Wizard_OpenAIChoice_WithEnvVarReference_WritesNoKeyAndTellsTheUser()
    {
        using var scratch = new ScriptScratch();
        var target = Path.Combine(scratch.Root, "appsettings.json");
        using var console = new TestConsole();
        // 3 = OpenAI, default model, empty = "yes, reference an env var", default env name.
        using var input = new StringReader("3\n\n\n\n");

        var exit = await InitCommand.ExecuteAsync(new InitCommandOptions
        {
            OutputPath = target,
            NoProbe = true,
            InputOverride = input,
        });

        Assert.Equal(Program.ExitOk, exit);
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
        var llm = doc.RootElement.GetProperty("Llm");
        Assert.Equal("https://api.openai.com/v1", llm.GetProperty("BaseUrl").GetString());
        Assert.Equal(ProviderDefaults.ForProvider("openai"), llm.GetProperty("Model").GetString());
        // Env-var reference: the key never lands in the file, the user is told where to set it.
        Assert.False(llm.TryGetProperty("ApiKey", out _));
        Assert.Contains("ORKEON_Llm__ApiKey", console.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Wizard_CustomChoice_WithInlineKey_WarnsPlainText_AndNeverEchoesTheSecret()
    {
        using var scratch = new ScriptScratch();
        var target = Path.Combine(scratch.Root, "appsettings.json");
        const string secret = "sk-wizard-secret-never-echoed-42";
        using var console = new TestConsole();
        // 4 = custom, base URL, model, "n" = inline key, then the key itself.
        using var input = new StringReader($"4\nhttps://api.deepseek.com\ndeepseek-v4-flash\nn\n{secret}\n");

        var exit = await InitCommand.ExecuteAsync(new InitCommandOptions
        {
            OutputPath = target,
            NoProbe = true,
            InputOverride = input,
        });

        Assert.Equal(Program.ExitOk, exit);
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
        var llm = doc.RootElement.GetProperty("Llm");
        Assert.Equal("https://api.deepseek.com", llm.GetProperty("BaseUrl").GetString());
        Assert.Equal("deepseek-v4-flash", llm.GetProperty("Model").GetString());
        Assert.Equal(secret, llm.GetProperty("ApiKey").GetString());
        // Plain-text storage warned about (wizard prompt + post-write guidance), secret never echoed.
        Assert.Contains("plain text", console.Stdout + console.Stderr, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(secret, console.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Wizard_CustomChoice_WithEnvVarReference_WritesNoKey()
    {
        using var scratch = new ScriptScratch();
        var target = Path.Combine(scratch.Root, "appsettings.json");
        using var console = new TestConsole();
        // 4 = custom, base URL, model, "y" = env var reference, custom env name.
        using var input = new StringReader("4\nhttps://api.mistral.ai/v1\nmistral-medium\ny\nMY_LLM_KEY\n");

        var exit = await InitCommand.ExecuteAsync(new InitCommandOptions
        {
            OutputPath = target,
            NoProbe = true,
            InputOverride = input,
        });

        Assert.Equal(Program.ExitOk, exit);
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
        var llm = doc.RootElement.GetProperty("Llm");
        Assert.Equal("https://api.mistral.ai/v1", llm.GetProperty("BaseUrl").GetString());
        Assert.False(llm.TryGetProperty("ApiKey", out _));
        // STUDIO-49: the file names the variable, and the runtime reads it — the key stays out.
        Assert.Equal("MY_LLM_KEY", llm.GetProperty("ApiKeyEnvVar").GetString());
        Assert.Contains("MY_LLM_KEY", console.Stdout, StringComparison.Ordinal);
    }

    // --- STUDIO-49: --api-key-env names the variable in the file, the runtime reads it ---

    [Fact]
    public async Task ApiKeyEnv_IsWrittenAsTheReference_TheRuntimeReads()
    {
        using var scratch = new ScriptScratch();
        var target = Path.Combine(scratch.Root, "appsettings.json");
        using var console = new TestConsole();

        var exit = await InitCommand.ExecuteAsync(new InitCommandOptions
        {
            Provider = "openai",
            ApiKeyEnv = "OPENAI_API_KEY",
            OutputPath = target,
            NoProbe = true,
        });

        Assert.Equal(Program.ExitOk, exit);
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
        var llm = doc.RootElement.GetProperty("Llm");
        Assert.Equal("OPENAI_API_KEY", llm.GetProperty("ApiKeyEnvVar").GetString());
        Assert.False(llm.TryGetProperty("ApiKey", out _));
        Assert.Equal(["Model", "BaseUrl", "ApiKeyEnvVar"], llm.EnumerateObject().Select(p => p.Name));
        // The old note said the variable was only read by the probes — the trap this closes.
        Assert.DoesNotContain("only", console.Stdout, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("export OPENAI_API_KEY=", console.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheNativeVariable_IsReadWithoutAReference()
    {
        using var scratch = new ScriptScratch();
        var target = Path.Combine(scratch.Root, "appsettings.json");
        using var console = new TestConsole();

        var exit = await InitCommand.ExecuteAsync(new InitCommandOptions
        {
            Provider = "openai",
            ApiKeyEnv = "ORKEON_Llm__ApiKey",
            OutputPath = target,
            NoProbe = true,
        });

        Assert.Equal(Program.ExitOk, exit);
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
        var llm = doc.RootElement.GetProperty("Llm");
        Assert.False(llm.TryGetProperty("ApiKeyEnvVar", out _));
        Assert.False(llm.TryGetProperty("ApiKey", out _));
    }

    [Fact]
    public async Task Wizard_InvalidChoice_RetriesThenSucceeds()
    {
        using var scratch = new ScriptScratch();
        var target = Path.Combine(scratch.Root, "appsettings.json");
        using var console = new TestConsole();
        // Invalid "9" → retry prompt → "5" (offline) succeeds.
        using var input = new StringReader("9\n5\n");

        var exit = await InitCommand.ExecuteAsync(new InitCommandOptions
        {
            OutputPath = target,
            NoProbe = true,
            InputOverride = input,
        });

        Assert.Equal(Program.ExitOk, exit);
        Assert.Contains("Please answer", console.Stdout, StringComparison.Ordinal);
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
        Assert.False(doc.RootElement.TryGetProperty("Llm", out _));
    }

    [Fact]
    public async Task Wizard_EndOfInput_FailsCleanly()
    {
        using var scratch = new ScriptScratch();
        var target = Path.Combine(scratch.Root, "appsettings.json");
        using var console = new TestConsole();
        using var input = new StringReader("");

        var exit = await InitCommand.ExecuteAsync(new InitCommandOptions
        {
            OutputPath = target,
            NoProbe = true,
            InputOverride = input,
        });

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.False(File.Exists(target));
    }

    // ── GAP-36, decision 3: the probe presents the key the run presents ─────

    /// <summary>
    /// Runs <c>orkeon init --provider custom</c> against <paramref name="server"/> with the probe on,
    /// <c>ORKEON_Llm__ApiKey</c> set to <paramref name="nativeKey"/> for the call (null: unset) and
    /// restored after it — it belongs to every process of the machine.
    /// </summary>
    private static async Task<(int Exit, string Stdout, string Stderr, string Target)> InitWithProbeAsync(
        ScriptScratch scratch, CatalogueServer server, string apiKeyEnv, string? nativeKey)
    {
        var target = Path.Combine(scratch.Root, "appsettings.json");
        var saved = Environment.GetEnvironmentVariable("ORKEON_Llm__ApiKey");
        Environment.SetEnvironmentVariable("ORKEON_Llm__ApiKey", nativeKey);
        try
        {
            using var console = new TestConsole();
            var exit = await InitCommand.ExecuteAsync(new InitCommandOptions
            {
                Provider = "custom",
                BaseUrl = server.BaseUrl,
                Model = "test-model",
                ApiKeyEnv = apiKeyEnv,
                OutputPath = target,
            });
            return (exit, console.Stdout, console.Stderr, target);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ORKEON_Llm__ApiKey", saved);
        }
    }

    [Fact]
    public async Task TheProbe_PresentsTheKeyARunPresents_AndSaysWhereItComesFrom()
    {
        // A run takes the key the configuration resolves — ORKEON_Llm__ApiKey — before the variable
        // the file names: the probe read that variable first, in the process alone.
        var variable = "ORKEON_TEST_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(variable, "sk-named-variable-k2");
        try
        {
            using var scratch = new ScriptScratch();
            using var server = new CatalogueServer();

            var (exit, stdout, stderr, _) = await InitWithProbeAsync(scratch, server, variable, nativeKey: "sk-native-k1");

            Assert.Equal(Program.ExitOk, exit);
            Assert.Equal("Bearer sk-native-k1", Assert.Single(server.Authorizations));
            Assert.Contains("API key from configuration (Llm:ApiKey)", stdout, StringComparison.Ordinal);
            Assert.Contains("Probe OK", stdout, StringComparison.Ordinal);
            foreach (var text in new[] { stdout, stderr })
            {
                Assert.DoesNotContain("sk-native-k1", text, StringComparison.Ordinal);
                Assert.DoesNotContain("sk-named-variable-k2", text, StringComparison.Ordinal);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public async Task TheProbe_ReadsTheVariableTheFileNames_WhenTheConfigurationResolvesNoKey()
    {
        var variable = "ORKEON_TEST_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(variable, "sk-named-variable-k2");
        try
        {
            using var scratch = new ScriptScratch();
            using var server = new CatalogueServer();

            var (exit, stdout, _, _) = await InitWithProbeAsync(scratch, server, variable, nativeKey: null);

            Assert.Equal(Program.ExitOk, exit);
            Assert.Equal("Bearer sk-named-variable-k2", Assert.Single(server.Authorizations));
            Assert.Contains("API key from the variable named by Llm:ApiKeyEnvVar (process environment)", stdout, StringComparison.Ordinal);
            Assert.DoesNotContain("sk-named-variable-k2", stdout, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public async Task AReferenceSetNowhere_IsWarnedBeforeTheProbe_InTheSharedWording()
    {
        var unset = "ORKEON_TEST_" + Guid.NewGuid().ToString("N");
        using var scratch = new ScriptScratch();
        using var server = new CatalogueServer();

        var (exit, stdout, stderr, _) = await InitWithProbeAsync(scratch, server, unset, nativeKey: null);

        Assert.Equal(Program.ExitOk, exit);
        Assert.Contains(
            "WARNING: Llm:ApiKeyEnvVar names an environment variable that is not set: calls on that LLM profile answer that an API key is required.",
            stderr, StringComparison.Ordinal);
        // The probe still runs — the run would, keyless — and presents no key.
        Assert.Equal("", Assert.Single(server.Authorizations));
        Assert.Contains("API key none — the variable named by Llm:ApiKeyEnvVar is not set", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnLlmSectionARunRefuses_FailsTheProbeWithTheReadersMessage_AndTheFileIsWritten()
    {
        using var scratch = new ScriptScratch();
        using var server = new CatalogueServer();

        // A space: no variable's name — a key or a sentence pasted in the wrong field.
        var (exit, _, stderr, target) = await InitWithProbeAsync(scratch, server, "NOT A NAME", nativeKey: null);

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.True(File.Exists(target));
        Assert.Contains("Llm:ApiKeyEnvVar is not the name of an environment variable", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", stderr, StringComparison.Ordinal);
        Assert.Empty(server.Authorizations);
    }
}
