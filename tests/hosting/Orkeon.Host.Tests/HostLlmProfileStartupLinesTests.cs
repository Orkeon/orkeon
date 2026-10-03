using Microsoft.Extensions.Logging;
using Orkeon.Constants.FileSystem;
using Orkeon.Domain.FileSystem;
using Orkeon.Host.Tests.Doubles;
using Orkeon.Hosting;

namespace Orkeon.Host.Tests;

/// <summary>
/// GAP-36, decision 7 — <c>orkeon-host</c>'s startup lines follow its allow-list
/// (<c>Orkeon:Host:LlmProfiles</c>): they read the profiles of the settings file, so the line
/// "offered to crews" named the profiles the host hides too, and a hidden profile whose key
/// reference resolves nothing was warned about — on the log and on stderr — although no crew can
/// name it. They now name the profiles offered, each with its key's source, warn for those alone,
/// and one line names the hidden ones, so the operator sees the list at work. Without a list,
/// nothing changes. The host is the one <c>Program</c> builds: the runner host plus
/// <see cref="HostServiceRegistration.AddHostServices"/>, built and never started.
/// <para>
/// In the working-directory collection: the tests borrow the process-global stderr. The variables
/// the profiles name are never set.
/// </para>
/// </summary>
[Collection(nameof(WorkingDirectoryCollection))]
public sealed class HostLlmProfileStartupLinesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"orkeon-host-profile-lines-{Guid.NewGuid():N}");
    private readonly string _unsetA = "ORKEON_TEST_" + Guid.NewGuid().ToString("N");
    private readonly string _unsetB = "ORKEON_TEST_" + Guid.NewGuid().ToString("N");

    public HostLlmProfileStartupLinesTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private (IReadOnlyList<string> Lines, IReadOnlyList<string> Warnings, string Stderr) Build(string allowList)
    {
        var settings = Path.Combine(_root, "appsettings.json");
        File.WriteAllText(settings, $$"""
            {
              "Llm": {
                "BaseUrl": "http://localhost:11434", "Model": "qwen3",
                "Profiles": {
                  "a": { "BaseUrl": "https://api.deepseek.com/v1", "Model": "deepseek-chat", "ApiKeyEnvVar": "{{_unsetA}}" },
                  "b": { "BaseUrl": "https://api.moonshot.ai/v1", "Model": "kimi-k3", "ApiKeyEnvVar": "{{_unsetB}}" }
                }
              },
              "Orkeon": { "Host": { {{allowList}} } },
              "RaggableTree": { "Enabled": false }
            }
            """);

        using var logs = new RecordingLoggerProvider();
        var original = Console.Error;
        using var stderr = new StringWriter();
        Console.SetError(stderr);
        try
        {
            RunnerHost.Build(
                settings,
                new RunnerMountPlan { InternalMounts = [$"{FileSystemMount.Quote(_root)}:{RunnerVirtualRoots.Crew}:ro"] },
                configureLogging: (_, logging) =>
                {
                    logging.AddProvider(logs);
                    logging.SetMinimumLevel(LogLevel.Information);
                },
                configureServices: (context, services) =>
                    services.AddHostServices(context.Configuration, HostCrewMounts.For([])))
                .Dispose();
        }
        finally
        {
            Console.SetError(original);
        }

        var entries = logs.Logger.Entries.ToList();
        return (
            [.. entries.Select(e => e.Message)],
            [.. entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message)],
            stderr.ToString());
    }

    private static int CountUnresolvedLines(string stderr) =>
        stderr.Split('\n').Count(l => l.Contains("names an environment variable that is not set", StringComparison.Ordinal));

    [Fact]
    public void Under_an_allow_list_the_lines_name_the_offered_profiles_and_warn_for_them_alone()
    {
        var (lines, warnings, stderr) = Build("""
            "LlmProfiles": ["default", "a"]
            """);

        Assert.Contains("LLM profiles offered to crews besides the default: a", lines);
        Assert.Contains(lines, l => l.StartsWith("LLM profile a: apiKey=", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.StartsWith("LLM profile b:", StringComparison.Ordinal));

        Assert.Single(warnings, w => w.Contains("Llm:Profiles:a:ApiKeyEnvVar", StringComparison.Ordinal));
        Assert.DoesNotContain(warnings, w => w.Contains("Llm:Profiles:b:", StringComparison.Ordinal));
        Assert.Equal(1, CountUnresolvedLines(stderr));
        Assert.DoesNotContain("Llm:Profiles:b:", stderr, StringComparison.Ordinal);

        // The operator sees the list at work: one information line names the hidden profiles.
        Assert.Contains("LLM profiles hidden from crews by the host's allow-list: b", lines);
    }

    [Fact]
    public void Without_an_allow_list_nothing_changes()
    {
        var (lines, warnings, stderr) = Build("");

        Assert.Contains("LLM profiles offered to crews besides the default: a, b", lines);
        Assert.Contains(lines, l => l.StartsWith("LLM profile a: apiKey=", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("LLM profile b: apiKey=", StringComparison.Ordinal));
        Assert.Single(warnings, w => w.Contains("Llm:Profiles:a:ApiKeyEnvVar", StringComparison.Ordinal));
        Assert.Single(warnings, w => w.Contains("Llm:Profiles:b:ApiKeyEnvVar", StringComparison.Ordinal));
        Assert.Equal(2, CountUnresolvedLines(stderr));
        Assert.DoesNotContain(lines, l => l.StartsWith("LLM profiles hidden", StringComparison.Ordinal));
    }
}
