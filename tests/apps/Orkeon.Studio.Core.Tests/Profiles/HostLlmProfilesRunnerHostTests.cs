using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.FileSystem;
using Orkeon.Hosting;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Core.Storage;
using Orkeon.Studio.Core.Tests.Doubles;
using Orkeon.Studio.Core.Validation;

namespace Orkeon.Studio.Core.Tests.Profiles;

/// <summary>
/// STUDIO-48, end to end: a settings file Studio wrote — its model settings mirrored into
/// <c>Llm:Profiles</c>, an entry written by hand beside them, the RAG on one of them — passes the
/// profile validation the real runner host runs at start (<c>LlmSettings</c>, GAP-17, and the RAG
/// profile check of GAP-19), and every profile is built on the key the environment carries: the
/// one a Studio launch lays over its child, or — STUDIO-49 — the variable Studio remembers it in,
/// which the file names, for a terminal run or a scheduled team. A team launched on another
/// setting than the default never receives the default's key.
/// <para>
/// The variables these tests set carry a unique name (<c>ORKEON_TEST_&lt;guid&gt;</c>), which the
/// test's setting takes as its <see cref="ModelProfile.KeyEnvName"/>: the suites run in parallel.
/// </para>
/// </summary>
public sealed class HostLlmProfilesRunnerHostTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "orkeon-studio-host-profiles-" + Guid.NewGuid().ToString("N"));

    public HostLlmProfilesRunnerHostTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private static readonly ModelProfile DeepSeek = new()
    {
        Name = "DeepSeek",
        Provider = "DeepSeek",
        BaseUrl = "https://api.deepseek.com",
        Model = "deepseek-v4-flash",
        KeyEnvName = "DEEPSEEK_API_KEY",
        TimeoutSeconds = 600,
    };

    private static readonly ModelProfile Zai = new()
    {
        Name = "Z.AI",
        Provider = "Z.AI",
        BaseUrl = "https://api.z.ai/api/paas/v4",
        Model = "glm-5.2",
        KeyEnvName = "ZAI_API_KEY",
        ThinkingEnabled = false,
    };

    private static readonly ModelProfile Ollama = new()
    {
        Name = "Local",
        Provider = "Ollama",
        BaseUrl = "http://localhost:11434",
        Model = "qwen3",
    };

    private static readonly ModelProfile Docker = new()
    {
        Name = "Docker",
        Provider = "docker-model-runner",
        BaseUrl = "http://localhost:12434/engines/llama.cpp/v1",
        Model = "ai/granite-4.0-h-tiny",
    };

    private static string UniqueVariable() => "ORKEON_TEST_" + Guid.NewGuid().ToString("N");

    /// <summary>
    /// Writes the settings file Studio writes once <paramref name="settings"/> exist, the first one
    /// elected default — its whole entry in <c>Llm</c> (STUDIO-49) — and returns its path.
    /// </summary>
    private async Task<(string Path, ModelProfileSet Set)> WriteElectedSettingsAsync(params ModelProfile[] settings)
    {
        var document = AppSettingsDocument.Parse("""{ "RaggableTree": { "Enabled": false } }""");
        var set = settings.Aggregate(ModelProfileSet.Empty, (current, setting) => current.Upsert(setting));
        HostLlmProfiles.Mirror(document, ModelProfileSet.Empty, set);
        HostLlmProfiles.ElectDefault(document, set.Default!);

        var path = Path.Combine(_root, AppSettingsDocument.FileName);
        await AppSettingsFile.SaveAsync(document, path, TestContext.Current.CancellationToken);
        KeyTripwire.AssertNamesNoKey(
            await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken),
            [.. settings.Select(setting => setting.KeyEnvName).OfType<string>()]);
        return (path, set);
    }

    /// <summary>Writes the settings file the way Studio does, and returns its path.</summary>
    private async Task<(string Path, ModelProfileSet Set)> WriteStudioSettingsAsync()
    {
        var document = AppSettingsDocument.Parse("""
            {
              "Llm": {
                "BaseUrl": "http://localhost:11434", "Model": "qwen3",
                "Profiles": { "local-gpu": { "BaseUrl": "http://localhost:11500", "Model": "qwen3:32b" } }
              },
              "RaggableTree": { "Enabled": false }
            }
            """);
        var set = ModelProfileSet.Empty.Upsert(DeepSeek).Upsert(Zai);
        HostLlmProfiles.Mirror(document, ModelProfileSet.Empty, set);
        document.Rag.LlmProfile = "z-ai";

        var path = Path.Combine(_root, AppSettingsDocument.FileName);
        await AppSettingsFile.SaveAsync(document, path, TestContext.Current.CancellationToken);
        return (path, set);
    }

    /// <summary>
    /// Builds the runner host over <paramref name="settingsPath"/> with <paramref name="environment"/>
    /// laid last, as the child process reads it: <c>ORKEON_A__B</c> is the key <c>A:B</c> — the
    /// .NET rule <c>AddEnvironmentVariables("ORKEON_")</c> applies, which cannot be fed a dictionary.
    /// </summary>
    private IHost Build(string settingsPath, IReadOnlyDictionary<string, string> environment)
    {
        var overlay = environment.ToDictionary(
            entry => entry.Key["ORKEON_".Length..].Replace("__", ":", StringComparison.Ordinal),
            entry => (string?)entry.Value,
            StringComparer.OrdinalIgnoreCase);

        return RunnerHost.Build(
            settingsPath,
            new RunnerMountPlan
            {
                CliMounts = [$"{FileSystemMount.Quote(_root)}:/crew:ro"],
                AllowExternalMounts = true,
            },
            configureLogging: (_, logging) => logging.SetMinimumLevel(LogLevel.None),
            configureBuilder: builder => builder.ConfigureAppConfiguration(
                (_, configuration) => configuration.AddInMemoryCollection(overlay)));
    }

    [Fact]
    public async Task A_file_studio_wrote_starts_the_host_and_a_studio_launch_brings_each_profile_its_key()
    {
        var (settingsPath, set) = await WriteStudioSettingsAsync();
        KeyTripwire.AssertNamesNoKey(
            await File.ReadAllTextAsync(settingsPath, TestContext.Current.CancellationToken), "DEEPSEEK_API_KEY", "ZAI_API_KEY");
        var keys = new FakeApiKeyStore();
        keys.Stage("DEEPSEEK_API_KEY", "sk-ds");
        keys.Stage("ZAI_API_KEY", "sk-zai");

        using var host = Build(settingsPath, HostLlmProfiles.LaunchEnvironment(set, keys.Peek));

        var profiles = host.Services.GetRequiredService<ILlmProfileRegistry>();
        // The configuration binder lists a section's children sorted by key.
        Assert.Equal(["deepseek", "local-gpu", "z-ai"], profiles.Names);
        var deepseek = profiles.Resolve("deepseek").Provider.BaseConfig!;
        Assert.Equal("sk-ds", deepseek.ApiKey);
        Assert.Equal("deepseek-v4-flash", deepseek.Model);
        Assert.Equal(new Uri("https://api.deepseek.com"), deepseek.BaseUrl);
        Assert.Equal(600, deepseek.TimeoutSeconds);
        var zai = profiles.Resolve("z-ai").Provider.BaseConfig!;
        Assert.Equal("sk-zai", zai.ApiKey);
        Assert.False(zai.Thinking?.Enabled);
        // The entry written by hand is offered beside them, untouched.
        Assert.Equal("qwen3:32b", profiles.Resolve("local-gpu").Provider.BaseConfig!.Model);
    }

    [Fact]
    public async Task A_terminal_run_on_the_same_file_needs_only_the_variable_studio_remembers_each_key_in()
    {
        // STUDIO-49: no ORKEON_Llm__* at all — a terminal, a scheduled team: only the provider's
        // variables, where Studio remembers the keys, which the file names.
        var deepseekVariable = UniqueVariable();
        var zaiVariable = UniqueVariable();
        var (settingsPath, _) = await WriteElectedSettingsAsync(
            DeepSeek with { KeyEnvName = deepseekVariable }, Zai with { KeyEnvName = zaiVariable });
        Environment.SetEnvironmentVariable(deepseekVariable, "sk-ds-terminal");
        Environment.SetEnvironmentVariable(zaiVariable, "sk-zai-terminal");
        try
        {
            using var host = Build(settingsPath, new Dictionary<string, string>(StringComparer.Ordinal));

            var profiles = host.Services.GetRequiredService<ILlmProfileRegistry>();
            var deepseek = profiles.Resolve("deepseek").Provider.BaseConfig!;
            Assert.Equal("sk-ds-terminal", deepseek.ApiKey);
            Assert.Equal("deepseek-v4-flash", deepseek.Model);
            Assert.Equal(new Uri("https://api.deepseek.com"), deepseek.BaseUrl);
            Assert.Equal("sk-zai-terminal", profiles.Resolve("z-ai").Provider.BaseConfig!.ApiKey);

            // The default is the election, written whole: its key the same way, and its 600 s.
            var elected = profiles.Resolve(null).Provider.BaseConfig!;
            Assert.Equal("sk-ds-terminal", elected.ApiKey);
            Assert.Equal(new Uri("https://api.deepseek.com"), elected.BaseUrl);
            Assert.Equal(600, elected.TimeoutSeconds);
        }
        finally
        {
            Environment.SetEnvironmentVariable(deepseekVariable, null);
            Environment.SetEnvironmentVariable(zaiVariable, null);
        }
    }

    /// <summary>
    /// Decision 4, with the real runner host: a team launched on another setting than the default
    /// lays every default field, blank when its setting leaves it unset — the default's key and
    /// the variable holding it first. A team on Z.AI whose key is not remembered fails without a
    /// key instead of sending the DeepSeek key of the default to Z.AI.
    /// </summary>
    [Theory]
    [InlineData("Z.AI", false)]
    [InlineData("Local", false)]
    [InlineData("Z.AI", true)]
    public async Task A_team_launched_on_another_setting_never_receives_the_key_of_the_default(string teamSetting, bool keyRemembered)
    {
        var deepseekVariable = UniqueVariable();
        // Unique, and never set: a machine that really holds ZAI_API_KEY must not answer for Studio.
        var zaiVariable = UniqueVariable();
        var (settingsPath, set) = await WriteElectedSettingsAsync(
            DeepSeek with { KeyEnvName = deepseekVariable }, Zai with { KeyEnvName = zaiVariable }, Ollama);
        // The default's key, where Studio remembers it: in every process of the user.
        Environment.SetEnvironmentVariable(deepseekVariable, "sk-ds-default");
        try
        {
            var keys = new FakeApiKeyStore();
            keys.Stage(deepseekVariable, "sk-ds-default");
            if (keyRemembered)
                keys.Stage(zaiVariable, "sk-zai");
            var team = set.Find(teamSetting)!;
            var launch = new Dictionary<string, string>(HostLlmProfiles.LaunchEnvironment(set, keys.Peek), StringComparer.Ordinal);
            foreach (var (key, value) in team.EnvironmentOverrides(keys.Peek))
                launch[key] = value;

            using var host = Build(settingsPath, launch);

            var runsOn = host.Services.GetRequiredService<ILlmProfileRegistry>().Resolve(null).Provider.BaseConfig!;
            Assert.Equal(new Uri(team.BaseUrl!), runsOn.BaseUrl);
            Assert.Equal(team.Model, runsOn.Model);
            Assert.Equal(keyRemembered ? "sk-zai" : null, runsOn.ApiKey);
            // What the team's setting leaves unset is not the default's either: no 600 s.
            Assert.Null(runsOn.TimeoutSeconds);
        }
        finally
        {
            Environment.SetEnvironmentVariable(deepseekVariable, null);
        }
    }

    /// <summary>
    /// STUDIO-54, decision 4, with the real runner host: the run reads Docker Model Runner as
    /// OpenAI, whose dialect refuses to call without a key. On the file Studio writes, the elected
    /// default and the <c>docker</c> profile are built with the placeholder <c>not-needed</c> — no
    /// variable anywhere: a terminal, a scheduled team.
    /// </summary>
    [Fact]
    public async Task A_docker_model_runner_setting_runs_from_the_file_alone_with_its_placeholder_key()
    {
        var deepseekVariable = UniqueVariable();
        var (settingsPath, _) = await WriteElectedSettingsAsync(Docker, DeepSeek with { KeyEnvName = deepseekVariable });

        using var host = Build(settingsPath, new Dictionary<string, string>(StringComparer.Ordinal));

        var profiles = host.Services.GetRequiredService<ILlmProfileRegistry>();
        Assert.Equal("not-needed", profiles.Resolve("docker").Provider.BaseConfig!.ApiKey);
        var elected = profiles.Resolve(null).Provider.BaseConfig!;
        Assert.Equal(new Uri(Docker.BaseUrl!), elected.BaseUrl);
        Assert.Equal("not-needed", elected.ApiKey);
    }

    /// <summary>A team launched on the Docker Model Runner setting in place of the elected DeepSeek carries the placeholder too.</summary>
    [Fact]
    public async Task A_team_launched_on_a_docker_model_runner_setting_carries_its_placeholder_key()
    {
        var deepseekVariable = UniqueVariable();
        var (settingsPath, set) = await WriteElectedSettingsAsync(DeepSeek with { KeyEnvName = deepseekVariable }, Docker);
        Environment.SetEnvironmentVariable(deepseekVariable, "sk-ds-default");
        try
        {
            var keys = new FakeApiKeyStore();
            keys.Stage(deepseekVariable, "sk-ds-default");
            var launch = new Dictionary<string, string>(HostLlmProfiles.LaunchEnvironment(set, keys.Peek), StringComparer.Ordinal);
            foreach (var (key, value) in set.Find("Docker")!.EnvironmentOverrides(keys.Peek))
                launch[key] = value;

            using var host = Build(settingsPath, launch);

            var runsOn = host.Services.GetRequiredService<ILlmProfileRegistry>().Resolve(null).Provider.BaseConfig!;
            Assert.Equal(new Uri(Docker.BaseUrl!), runsOn.BaseUrl);
            Assert.Equal("not-needed", runsOn.ApiKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable(deepseekVariable, null);
        }
    }

    /// <summary>Values of the <c>Llm</c> shape the real host refuses at start (GAP-40), at their path.</summary>
    public static TheoryData<string, string> ValuesTheHostRefuses() => new()
    {
        { "Llm:Profiles:local-gpu:Temperature", "\"warm\"" },
        { "Llm:Profiles:local-gpu:MaxTokens", "600.0" },
        { "Llm:Profiles:local-gpu:TimeoutSeconds", "\"600s\"" },
        { "Llm:Profiles:local-gpu:MaxRetries", "1.5" },
        { "Llm:Profiles:local-gpu:BaseUrl", "\"not a url\"" },
        { "Llm:Profiles:local-gpu:ApiKeyEnvVar", "\"MY KEY\"" },
        { "Llm:Profiles:local-gpu:ApiKey", "\"${LOCAL_KEY}\"" },
        { "Llm:Profiles:local-gpu:Grammar", "\"yes\"" },
        { "Llm:Temperature", "\"Infinity\"" },
        { "Llm:MaxTokens", "\"4096x\"" },
        { "Llm:TimeoutSeconds", "600.5" },
        { "Llm:ApiKey", "\"${OPENAI_API_KEY}\"" },
        { "Llm:Thinking:Enabled", "\"on\"" },
    };

    /// <summary>
    /// STUDIO-55, the drift guard: each value of <c>Llm</c> or of an entry of <c>Llm:Profiles</c> that
    /// the real runner host refuses at start is refused by Studio's check before saving, at the same
    /// path — Studio.Core copies the run's rules, and this test holds the copy to the original.
    /// </summary>
    [Theory]
    [MemberData(nameof(ValuesTheHostRefuses))]
    public async Task What_the_host_refuses_at_start_studio_refuses_before_saving_at_the_same_path(string path, string json)
    {
        var document = AppSettingsDocument.Parse("""
            {
              "Llm": {
                "BaseUrl": "http://localhost:11434", "Model": "qwen3",
                "Profiles": { "local-gpu": { "BaseUrl": "http://localhost:11500", "Model": "qwen3:32b" } }
              },
              "RaggableTree": { "Enabled": false }
            }
            """);
        document.SetNode(path, System.Text.Json.Nodes.JsonNode.Parse(json));
        var settingsPath = Path.Combine(_root, AppSettingsDocument.FileName);
        await AppSettingsFile.SaveAsync(document, settingsPath, TestContext.Current.CancellationToken);

        Assert.Throws<RunnerSettingsException>(() => Build(settingsPath, new Dictionary<string, string>(StringComparer.Ordinal)));
        Assert.Contains(
            new AppSettingsValidator(new FakeDirectoryProbe()).Validate(document),
            message => message.Severity == ValidationSeverity.Error && message.Path == path);
    }

    /// <summary>The reserved name: an entry <c>default</c> written by hand refuses the host, and Studio's check.</summary>
    [Fact]
    public async Task An_entry_named_default_refuses_the_host_and_studio_refuses_it_before_saving()
    {
        var document = AppSettingsDocument.Parse("""
            {
              "Llm": {
                "BaseUrl": "http://localhost:11434", "Model": "qwen3",
                "Profiles": { "default": { "BaseUrl": "http://localhost:11500", "Model": "qwen3:32b" } }
              },
              "RaggableTree": { "Enabled": false }
            }
            """);
        var settingsPath = Path.Combine(_root, AppSettingsDocument.FileName);
        await AppSettingsFile.SaveAsync(document, settingsPath, TestContext.Current.CancellationToken);

        Assert.Throws<RunnerSettingsException>(() => Build(settingsPath, new Dictionary<string, string>(StringComparer.Ordinal)));
        Assert.Contains(
            new AppSettingsValidator(new FakeDirectoryProbe()).Validate(document),
            message => message.Code == ValidationCodes.LlmProfileReservedName && message.Path == "Llm:Profiles:default");
    }
}
