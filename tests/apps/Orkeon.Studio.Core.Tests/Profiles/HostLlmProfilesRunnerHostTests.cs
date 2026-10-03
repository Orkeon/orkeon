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

namespace Orkeon.Studio.Core.Tests.Profiles;

/// <summary>
/// STUDIO-48, end to end: a settings file Studio wrote — its model settings mirrored into
/// <c>Llm:Profiles</c>, an entry written by hand beside them, the RAG on one of them — passes the
/// profile validation the real runner host runs at start (<c>LlmSettings</c>, GAP-17, and the RAG
/// profile check of GAP-19), and every profile is built on the key the environment carries: the
/// one a Studio launch lays over its child, or the one variable a terminal user sets.
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
        Assert.DoesNotContain("ApiKey", await File.ReadAllTextAsync(settingsPath, TestContext.Current.CancellationToken),
            StringComparison.OrdinalIgnoreCase);
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
    public async Task A_terminal_run_on_the_same_file_needs_only_the_key_variable_of_each_profile()
    {
        var (settingsPath, _) = await WriteStudioSettingsAsync();
        var terminal = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ModelProfile.HostKeyVariable("deepseek")] = "sk-ds-terminal",
        };

        using var host = Build(settingsPath, terminal);

        var deepseek = host.Services.GetRequiredService<ILlmProfileRegistry>().Resolve("deepseek").Provider.BaseConfig!;
        Assert.Equal("sk-ds-terminal", deepseek.ApiKey);
        Assert.Equal("deepseek-v4-flash", deepseek.Model);
        Assert.Equal(new Uri("https://api.deepseek.com"), deepseek.BaseUrl);
    }
}
