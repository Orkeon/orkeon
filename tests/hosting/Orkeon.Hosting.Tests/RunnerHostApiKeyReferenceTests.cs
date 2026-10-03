using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Hosting.Tests.Doubles;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// STUDIO-49 through the runner host: a section of the <c>Llm</c> shape names the variable that
/// holds its key (<c>ApiKeyEnvVar</c>), and the host reads it. The startup lines say where each
/// key came from — the configuration, the variable, the variable in the user environment, none —
/// and never the key nor the variable's name; a reference to a variable set nowhere is warned
/// about once per host build, by its configuration path.
/// <para>
/// The variables these tests set carry a unique name (<c>ORKEON_TEST_&lt;guid&gt;</c>): the suites
/// run in parallel, and no test writes the user scope.
/// </para>
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class RunnerHostApiKeyReferenceTests : IDisposable
{
    private readonly string _root;
    private readonly string _setVariable = "ORKEON_TEST_" + Guid.NewGuid().ToString("N");
    private readonly string _unsetVariable = "ORKEON_TEST_" + Guid.NewGuid().ToString("N");
    private const string Key = "sk-from-the-variable-0123456789";

    public RunnerHostApiKeyReferenceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "orkeon-key-reference-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        Environment.SetEnvironmentVariable(_setVariable, Key);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(_setVariable, null);
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private (IHost Host, CapturingLoggerProvider Logs, string Stderr) Build(string settingsJson)
    {
        var settingsPath = Path.Combine(_root, "appsettings.json");
        File.WriteAllText(settingsPath, settingsJson);
        var logs = new CapturingLoggerProvider();
        var original = Console.Error;
        using var stderr = new StringWriter();
        Console.SetError(stderr);
        try
        {
            var host = RunnerHost.Build(
                settingsPath: settingsPath,
                mounts: new RunnerMountPlan(),
                configureLogging: (_, b) =>
                {
                    b.AddProvider(logs);
                    b.SetMinimumLevel(LogLevel.Information);
                });
            return (host, logs, stderr.ToString());
        }
        finally
        {
            Console.SetError(original);
        }
    }

    private string Settings(string? defaultVariable = null) => $$"""
        {
          "Llm": {
            "BaseUrl": "https://api.deepseek.com", "Model": "deepseek-chat", "ApiKeyEnvVar": "{{defaultVariable ?? _setVariable}}",
            "Profiles": {
              "z-ai": { "BaseUrl": "https://api.z.ai/api/paas/v4", "Model": "glm-5", "ApiKeyEnvVar": "{{_setVariable}}" },
              "inline": { "BaseUrl": "https://api.anthropic.com/v1", "Model": "claude-sonnet-5", "ApiKey": "sk-inline-key" },
              "unset": { "BaseUrl": "https://api.moonshot.ai/v1", "Model": "kimi-k3", "ApiKeyEnvVar": "{{_unsetVariable}}" },
              "local": { "BaseUrl": "http://localhost:11434", "Model": "qwen3" }
            }
          }
        }
        """;

    [Fact]
    public void A_terminal_run_reads_the_key_of_each_section_from_the_variable_it_names()
    {
        var (host, logs, _) = Build(Settings());
        using (host)
        {
            var profiles = host.Services.GetRequiredService<ILlmProfileRegistry>();

#pragma warning disable CS0618 // ApiKey is the field every provider reads.
            Assert.Equal(Key, profiles.Resolve("z-ai").Provider.BaseConfig!.ApiKey);
            Assert.Equal("sk-inline-key", profiles.Resolve("inline").Provider.BaseConfig!.ApiKey);
            Assert.Null(profiles.Resolve("unset").Provider.BaseConfig!.ApiKey);
#pragma warning restore CS0618
        }

        Assert.NotEmpty(logs.Entries);
    }

    [Fact]
    public void The_startup_lines_say_where_each_key_came_from_never_the_key_nor_the_variable()
    {
        var (host, logs, stderr) = Build(Settings());
        host.Dispose();

        var lines = logs.Entries.Select(e => e.Message).ToList();
        Assert.Contains(lines, l => l.StartsWith("LLM resolved:", StringComparison.Ordinal)
            && l.Contains("apiKey=from the variable named by Llm:ApiKeyEnvVar (process environment)", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("LLM profile z-ai", StringComparison.Ordinal)
            && l.Contains("from the variable named by Llm:Profiles:z-ai:ApiKeyEnvVar (process environment)", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("LLM profile inline", StringComparison.Ordinal)
            && l.Contains("from configuration (Llm:Profiles:inline:ApiKey)", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("LLM profile unset", StringComparison.Ordinal)
            && l.Contains("not set", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("LLM profile local", StringComparison.Ordinal)
            && l.EndsWith("apiKey=none", StringComparison.Ordinal));

        foreach (var text in lines.Append(stderr))
        {
            Assert.DoesNotContain(Key, text, StringComparison.Ordinal);
            Assert.DoesNotContain("sk-inline-key", text, StringComparison.Ordinal);
            Assert.DoesNotContain(_setVariable, text, StringComparison.Ordinal);
            Assert.DoesNotContain(_unsetVariable, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_reference_to_a_variable_set_nowhere_is_warned_once_per_section_by_its_path()
    {
        var (host, logs, stderr) = Build(Settings(defaultVariable: _unsetVariable));
        host.Dispose();

        var warnings = logs.Entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message).ToList();
        Assert.Single(warnings, w => w.Contains("Llm:ApiKeyEnvVar names an environment variable that is not set", StringComparison.Ordinal));
        Assert.Single(warnings, w => w.Contains("Llm:Profiles:unset:ApiKeyEnvVar names an environment variable that is not set", StringComparison.Ordinal));
        Assert.DoesNotContain(warnings, w => w.Contains("z-ai", StringComparison.Ordinal) || w.Contains("inline", StringComparison.Ordinal));
        // stderr is the guarantee channel, as for WIN-01: one line per section.
        Assert.Equal(2, stderr.Split('\n').Count(l => l.Contains("names an environment variable that is not set", StringComparison.Ordinal)));
        Assert.DoesNotContain(_unsetVariable, stderr, StringComparison.Ordinal);
        Assert.DoesNotContain(warnings, w => w.Contains(_unsetVariable, StringComparison.Ordinal));
    }

    [Fact]
    public void An_ApiKey_written_as_a_placeholder_refuses_the_host_start_with_the_fix()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Build("""
            { "Llm": { "BaseUrl": "https://api.deepseek.com", "ApiKey": "${DEEPSEEK_API_KEY}" } }
            """).Host.Dispose());

        Assert.Contains("\"ApiKeyEnvVar\": \"DEEPSEEK_API_KEY\"", error.Message, StringComparison.Ordinal);
    }
}
