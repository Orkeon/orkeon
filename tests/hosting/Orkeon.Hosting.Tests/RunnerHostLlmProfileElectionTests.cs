using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel;
using Orkeon.Hosting.Tests.Doubles;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// STUDIO-50: <c>orkeon run --llm-profile &lt;id&gt;</c> elects one of the host's profiles
/// (<c>Llm:Profiles:&lt;id&gt;</c>) as the run's default. The <c>Llm</c> section becomes that
/// profile, whole — every field the profile leaves unset is unset, the default's key and the
/// variable holding it included —, so every call on the default (an agent that names no profile,
/// the planner, the RAG, <c>llm.default_</c>) reaches the profile's provider; the profiles stay
/// offered by name. An id the settings do not define refuses the run, listing the profiles they
/// do, as a crew naming an unknown profile fails its load.
/// <para>
/// The providers are <see cref="RecordingLlmProviderFactory"/>'s stubs: one per configuration
/// built, named after its model, so a test reads which one a call reached. The variables these
/// tests set carry a unique name (<c>ORKEON_TEST_&lt;guid&gt;</c>): the suites run in parallel.
/// </para>
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class RunnerHostLlmProfileElectionTests : IDisposable
{
    private sealed class TestOptions : RunnerOptionsBase { }

    private readonly string _root;
    private readonly string _keyVariable = "ORKEON_TEST_" + Guid.NewGuid().ToString("N");
    private const string ProfileKey = "sk-profile-b-0123456789";

    public RunnerHostLlmProfileElectionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "orkeon-llm-profile-election-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        Environment.SetEnvironmentVariable(_keyVariable, ProfileKey);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(_keyVariable, null);
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    /// <summary>
    /// A default the elected profile must not leak into: a clear-text key, a timeout, a
    /// temperature and a thinking switch the profile does not set.
    /// </summary>
    private string Settings() => $$"""
        {
          "Llm": {
            "Model": "model-default", "ApiKey": "sk-default-key", "TimeoutSeconds": 600, "Temperature": 0.2,
            "Thinking": { "Enabled": true },
            "Profiles": {
              "b": { "Model": "model-b", "ApiKeyEnvVar": "{{_keyVariable}}", "MaxTokens": 2048 },
              "c": { "Model": "model-c" }
            }
          },
          "RaggableTree": { "Enabled": false }
        }
        """;

    private string WriteSettings(string json)
    {
        var path = Path.Combine(_root, "appsettings.json");
        File.WriteAllText(path, json);
        return path;
    }

    private (IHost Host, RecordingLlmProviderFactory Providers, CapturingLoggerProvider Logs) Build(string? llmProfile, string? settingsJson = null)
    {
        var settingsPath = WriteSettings(settingsJson ?? Settings());
        var providers = new RecordingLlmProviderFactory();
        var logs = new CapturingLoggerProvider();
        var original = Console.Error;
        using var muted = new StringWriter();
        Console.SetError(muted);
        try
        {
            var host = RunnerHost.Build(
                settingsPath,
                new RunnerMountPlan(),
                configureLogging: (_, b) =>
                {
                    b.AddProvider(logs);
                    b.SetMinimumLevel(LogLevel.Information);
                },
                configureServices: (_, services) => services.AddSingleton<ILlmProviderFactory>(providers),
                llmProfile: llmProfile);
            return (host, providers, logs);
        }
        finally
        {
            Console.SetError(original);
        }
    }

    [Fact]
    public async Task A_call_on_the_default_reaches_the_elected_profiles_provider_and_never_the_defaults()
    {
        var (host, providers, _) = Build("b");
        using (host)
        {
            var chat = host.Services.GetRequiredService<IChatClient>();
            await chat.GetResponseAsync("Say hello.", cancellationToken: TestContext.Current.CancellationToken);

            Assert.Single(providers.For("model-b")!.ChatCalls);
            Assert.Null(providers.For("model-default"));

            // Every surface of the default is the elected profile: the provider an agent's loop
            // reaches, and the registry's default (the planner, the RAG, llm.default_).
            Assert.Equal("model-b", host.Services.GetRequiredService<ILlmProvider>().Name);
            Assert.Equal("model-b", host.Services.GetRequiredService<ILlmProfileRegistry>().Resolve(null).Provider.Name);
            // The profiles stay offered by name.
            Assert.Equal(["b", "c"], host.Services.GetRequiredService<ILlmProfileRegistry>().Names);
        }
    }

    [Fact]
    public void The_default_becomes_the_profile_whole_nothing_of_the_former_default_survives()
    {
        var (host, providers, _) = Build("b");
        using (host)
        {
            _ = host.Services.GetRequiredService<IBasicLlmProvider>();
            var config = providers.For("model-b")!.BaseConfig!;

            // The key is the profile's, read through the variable it names — never the default's.
            Assert.Equal(ProfileKey, config.ApiKey);
            Assert.Equal(2048, config.MaxTokens);
            // What the profile leaves unset is unset: the default's 600 s, its temperature and its
            // thinking switch do not ride along.
            Assert.Null(config.TimeoutSeconds);
            Assert.Null(config.Thinking);
            Assert.NotEqual(0.2, config.Temperature);

            // The configuration says the same thing to every reader of the Llm section — the
            // endpoint probe before a kickoff, the startup line.
            var configuration = host.Services.GetRequiredService<IConfiguration>();
            Assert.Equal("model-b", configuration["Llm:Model"]);
            Assert.True(string.IsNullOrEmpty(configuration["Llm:ApiKey"]));
            Assert.True(string.IsNullOrEmpty(configuration["Llm:TimeoutSeconds"]));
            Assert.Equal(_keyVariable, configuration["Llm:ApiKeyEnvVar"]);
            Assert.Equal("model-c", configuration["Llm:Profiles:c:Model"]);
        }
    }

    [Fact]
    public void Without_the_option_and_with_the_reserved_default_name_the_Llm_section_stays_the_default()
    {
        foreach (var llmProfile in new[] { null, "default", "DEFAULT" })
        {
            var (host, providers, _) = Build(llmProfile);
            using (host)
            {
                Assert.Equal("model-default", host.Services.GetRequiredService<ILlmProvider>().Name);
                Assert.Null(providers.For("model-b"));
            }
        }
    }

    [Fact]
    public void The_id_is_matched_without_regard_to_case_like_the_profiles_a_crew_names()
    {
        var (host, _, _) = Build("B");
        using (host)
            Assert.Equal("model-b", host.Services.GetRequiredService<ILlmProvider>().Name);
    }

    [Fact]
    public void The_startup_lines_say_which_profile_the_run_elected_and_where_its_key_comes_from()
    {
        var (host, _, logs) = Build("b");
        host.Dispose();

        var lines = logs.Entries.Select(e => e.Message).ToList();
        Assert.Contains(lines, l => l.Contains("LLM profile b elected as the run's default (--llm-profile)", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("LLM resolved:", StringComparison.Ordinal)
            && l.Contains("model=model-b", StringComparison.Ordinal)
            && l.Contains("apiKey=from the variable named by Llm:Profiles:b:ApiKeyEnvVar (process environment)", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains(ProfileKey, StringComparison.Ordinal) || l.Contains(_keyVariable, StringComparison.Ordinal));
    }

    [Fact]
    public void An_unresolved_key_of_the_elected_profile_is_warned_once_under_the_profiles_own_path()
    {
        var unset = "ORKEON_TEST_" + Guid.NewGuid().ToString("N");
        var (host, _, logs) = Build("b", Settings().Replace(_keyVariable, unset, StringComparison.Ordinal));
        host.Dispose();

        var warnings = logs.Entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message).ToList();
        Assert.Single(warnings, w => w.Contains("Llm:Profiles:b:ApiKeyEnvVar names an environment variable that is not set", StringComparison.Ordinal));
        // The Llm section carries the same reference only because it IS profile b for this run.
        Assert.DoesNotContain(warnings, w => w.Contains("Llm:ApiKeyEnvVar names", StringComparison.Ordinal));
    }

    [Fact]
    public void A_host_built_with_an_unknown_id_is_refused_listing_the_profiles_the_settings_define()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Build("claud"));

        Assert.Contains("--llm-profile names the LLM profile 'claud'", error.Message, StringComparison.Ordinal);
        Assert.Contains("Known profiles: default, b, c.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_runner_guard_refuses_an_unknown_id_in_one_line_and_accepts_a_known_one()
    {
        var settingsPath = WriteSettings(Settings());
        var original = Console.Error;
        using var stderr = new StringWriter();
        Console.SetError(stderr);
        try
        {
            Assert.True(RunnerExecution.EnsureLlmProfileIsKnown(null, settingsPath));
            Assert.True(RunnerExecution.EnsureLlmProfileIsKnown("default", settingsPath));
            Assert.True(RunnerExecution.EnsureLlmProfileIsKnown("c", settingsPath));
            Assert.Equal("", stderr.ToString());

            Assert.False(RunnerExecution.EnsureLlmProfileIsKnown("claud", settingsPath));
            // No settings file at all: no profile to elect, and the line says so the same way.
            Assert.False(RunnerExecution.EnsureLlmProfileIsKnown("b", settingsPath: null));
        }
        finally
        {
            Console.SetError(original);
        }

        var lines = stderr.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToList();
        Assert.Equal(2, lines.Count);
        Assert.StartsWith("ERROR: --llm-profile names the LLM profile 'claud'", lines[0], StringComparison.Ordinal);
        Assert.Contains("Known profiles: default, b, c.", lines[0], StringComparison.Ordinal);
        Assert.Contains("Known profiles: default.", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_run_electing_a_profile_runs_a_crew_that_names_none_on_that_profiles_provider()
    {
        WriteSettings(Settings());
        var crew = Path.Combine(_root, "crew.yaml");
        await File.WriteAllTextAsync(crew, """
            name: "elected"
            goal: "A crew that names no profile"
            process: "sequential"
            agents:
              worker:
                role: "Worker"
                goal: "Work"
                backstory: "A minimal test agent."
                maxIter: 1
            tasks:
              do_work:
                description: "Do the work."
                expectedOutput: "The work."
                agent: "worker"
            """, TestContext.Current.CancellationToken);

        var providers = new RecordingLlmProviderFactory();
        var (exit, stderr) = await RunAsync(new TestOptions
        {
            ConfigPath = crew,
            AllowExternalMounts = true,
            LlmProfile = "b",
        }, providers);

        Assert.True(exit == 0, stderr);
        Assert.NotEmpty(providers.For("model-b")!.ChatCalls);
        Assert.Null(providers.For("model-default"));
    }

    [Fact]
    public async Task A_run_electing_an_unknown_profile_exits_1_before_any_host_naming_the_known_ones()
    {
        WriteSettings(Settings());
        var crew = Path.Combine(_root, "crew.yaml");
        await File.WriteAllTextAsync(crew, "name: x\ngoal: g\n", TestContext.Current.CancellationToken);

        var providers = new RecordingLlmProviderFactory();
        var (exit, stderr) = await RunAsync(new TestOptions
        {
            ConfigPath = crew,
            AllowExternalMounts = true,
            LlmProfile = "nope",
        }, providers);

        Assert.Equal(1, exit);
        Assert.Contains("ERROR: --llm-profile names the LLM profile 'nope'", stderr, StringComparison.Ordinal);
        Assert.Contains("Known profiles: default, b, c.", stderr, StringComparison.Ordinal);
        Assert.Empty(providers.Built);
    }

    private static async Task<(int Exit, string Stderr)> RunAsync(RunnerOptionsBase options, RecordingLlmProviderFactory providers)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter(new StringBuilder());
        using var stderr = new StringWriter(new StringBuilder());
        Console.SetOut(stdout);
        Console.SetError(stderr);
        try
        {
            var exit = await RunnerExecution.RunOneShotAsync(
                options,
                "test",
                configureServices: (_, services) => services.AddSingleton<ILlmProviderFactory>(providers),
                externalCt: TestContext.Current.CancellationToken);
            return (exit, stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }
}
