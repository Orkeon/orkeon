using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Scripting.Runtime;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// GAP-17 through the runner host: <c>Llm:Profiles:&lt;name&gt;</c> in the settings becomes a
/// profile crews can name, each built by the provider factory; a crew naming one the settings
/// do not define fails its load listing the defined ones; an invalid profile fails the host
/// build with its key.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class RunnerHostLlmProfilesTests : IDisposable
{
    private readonly string _root;

    public RunnerHostLlmProfilesTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "orkeon-llm-profiles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private Microsoft.Extensions.Hosting.IHost Build(string settingsJson)
    {
        var settingsPath = Path.Combine(_root, "appsettings.json");
        File.WriteAllText(settingsPath, settingsJson);
        var original = Console.Error;
        using var muted = new StringWriter();
        Console.SetError(muted);
        try
        {
            return RunnerHost.Build(
                settingsPath: settingsPath,
                // A mount, so the crew loader (which reads through the VFS) can be built.
                mounts: new RunnerMountPlan
                {
                    CliMounts = [$"{FileSystemMount.Quote(_root)}:/crew:ro"],
                    AllowExternalMounts = true,
                },
                configureLogging: (_, b) => b.SetMinimumLevel(LogLevel.None));
        }
        finally
        {
            Console.SetError(original);
        }
    }

    private const string TwoProfiles = """
        {
          "Llm": {
            "BaseUrl": "https://api.deepseek.com/v1", "ApiKey": "sk-ds", "Model": "deepseek-chat",
            "Profiles": {
              "claude": { "BaseUrl": "https://api.anthropic.com/v1", "ApiKey": "sk-ant", "Model": "claude-sonnet-5" },
              "local": { "BaseUrl": "http://localhost:11434", "Model": "qwen3" }
            }
          }
        }
        """;

    [Fact]
    public void Each_profile_of_the_settings_is_offered_and_built_by_the_provider_factory()
    {
        using var host = Build(TwoProfiles);
        var profiles = host.Services.GetRequiredService<ILlmProfileRegistry>();

        Assert.Equal(["claude", "local"], profiles.Names);
        Assert.IsType<AnthropicLlmProvider>(MeteredLlmProvider.Unwrap(profiles.Resolve("claude").Provider));
        Assert.IsType<OllamaLlmProvider>(MeteredLlmProvider.Unwrap(profiles.Resolve("local").Provider));
        // The Llm section itself stays the default profile.
        Assert.IsType<DeepSeekLlmProvider>(MeteredLlmProvider.Unwrap(profiles.Resolve(null).Provider));
    }

    [Fact]
    public void Profiles_alone_leave_the_default_on_the_echo_provider()
    {
        using var host = Build("""{ "Llm": { "Profiles": { "local": { "BaseUrl": "http://localhost:11434", "Model": "qwen3" } } } }""");

        Assert.IsType<UndefinedLlmProvider>(MeteredLlmProvider.Unwrap(host.Services.GetRequiredService<ILlmProvider>()));
        Assert.Equal(["local"], host.Services.GetRequiredService<ILlmProfileRegistry>().Names);
    }

    [Fact]
    public async Task A_crew_naming_a_profile_the_settings_do_not_define_fails_its_load()
    {
        using var host = Build(TwoProfiles);
        var ct = TestContext.Current.CancellationToken;
        await using var scope = host.Services.CreateAsyncScope();
        var config = await scope.ServiceProvider.GetRequiredService<ICrewDefinitionLoader>().LoadFromStringAsync("""
            name: typo
            goal: g
            process: sequential
            agents:
              worker:
                role: Worker
                goal: Work
                llm:
                  profile: claud
            tasks:
              work:
                description: Work
                expected_output: Done
                agent: worker
            """, ct);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => scope.ServiceProvider.GetRequiredService<ICrewFactory>().CreateFromConfigAsync(config, ct));

        Assert.Contains("'claud'", error.Message, StringComparison.Ordinal);
        Assert.Contains("Known profiles: default, claude, local.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_invalid_profile_fails_the_host_build_with_its_key()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Build(
            """{ "Llm": { "Model": "m", "Profiles": { "claude": { "BaseUrl": "anthropic" } } } }"""));

        Assert.Contains("Llm:Profiles:claude:BaseUrl", error.Message, StringComparison.Ordinal);
    }
}
