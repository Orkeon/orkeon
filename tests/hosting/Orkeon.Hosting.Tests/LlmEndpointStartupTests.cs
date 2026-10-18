using System.Text;
using Microsoft.Extensions.Logging;
using Orkeon.Hosting.Tests.Doubles;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// CH-08 / CH-09 (R12): the runner refuses an address no run serves, warns once per section about a
/// <c>custom</c> address with no path, and says the provider when the LLM is resolved. In the
/// console-serial collection: the runs redirect the process-global console.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class LlmEndpointStartupTests : IDisposable
{
    private sealed class TestOptions : RunnerOptionsBase;

    private const string Crew = """
        name: "endpoint"
        goal: "A crew the refused address never lets load"
        process: "sequential"
        agents:
          reader:
            role: "Reader"
            goal: "Read"
            backstory: "A minimal test agent."
            maxIter: 1
        tasks:
          read:
            description: "Read something."
            expectedOutput: "Something read."
            agent: "reader"
        """;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "orkeon-llm-endpoint-" + Guid.NewGuid().ToString("N"));

    public LlmEndpointStartupTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private string WriteSettings(string llm)
    {
        var path = Path.Combine(_root, "settings.json");
        File.WriteAllText(path, "{ \"RaggableTree\": { \"Enabled\": false }, \"Llm\": " + llm + " }");
        return path;
    }

    [Fact]
    [Trait("BR", "CH-09")]
    public async Task ShouldExitWithOne_WhenTheRunnerReadsAnAddressNoRunServes()
    {
        var settings = WriteSettings("""{ "BaseUrl": "localhost:11434", "Model": "m" }""");
        var crew = Path.Combine(_root, "crew.yaml");
        await File.WriteAllTextAsync(crew, Crew, TestContext.Current.CancellationToken);
        var options = new TestOptions
        {
            ConfigPath = crew,
            SettingsPath = settings,
            AllowExternalMounts = true,
            Validate = true,
        };

        var (exit, stdout, stderr) = await CaptureAsync(() => RunnerExecution.RunOneShotAsync(options, "Orkeon.Hosting.Tests"));

        Assert.DoesNotContain("VALIDATION OK", stdout, StringComparison.Ordinal);
        Assert.True(exit == 1, $"exit {exit} - stderr: {stderr}");
        var last = stderr.TrimEnd().Split('\n')[^1].TrimEnd('\r');
        Assert.StartsWith("ERROR: ", last, StringComparison.Ordinal);
        Assert.Contains("Llm:BaseUrl", last, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-09")]
    public async Task ShouldWarnOncePerSection_WhenACustomAddressHasNoPath()
    {
        var settings = WriteSettings("""
            { "BaseUrl": "http://localhost:8080", "Model": "m",
              "Profiles": { "local": { "BaseUrl": "http://localhost:11500", "Model": "m" } } }
            """);
        using var logs = new CapturingLoggerProvider();

        var stderr = await BuildAsync(settings, logs, LogLevel.Warning);

        var lines = stderr.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.StartsWith("WARNING:", StringComparison.Ordinal)).ToList();
        Assert.Single(lines, l => l.Contains("Llm:BaseUrl", StringComparison.Ordinal));
        Assert.Single(lines, l => l.Contains("Llm:Profiles:local:BaseUrl", StringComparison.Ordinal));
        var warned = logs.Entries.Where(e => e.Level == LogLevel.Warning).ToList();
        Assert.Single(warned, e => e.Message.Contains("Llm:BaseUrl", StringComparison.Ordinal));
        Assert.Single(warned, e => e.Message.Contains("Llm:Profiles:local:BaseUrl", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("BR", "CH-08")]
    public async Task ShouldSayTheProvider_WhenTheLlmIsResolved()
    {
        var settings = WriteSettings("""{ "BaseUrl": "http://localhost:8080/v1", "Model": "m" }""");
        using var logs = new CapturingLoggerProvider();

        await BuildAsync(settings, logs, LogLevel.Information);

        var resolved = Assert.Single(logs.Entries, e => e.Message.StartsWith("LLM resolved:", StringComparison.Ordinal));
        Assert.Contains("provider=custom", resolved.Message, StringComparison.Ordinal);
    }

    private static async Task<string> BuildAsync(string settings, CapturingLoggerProvider logs, LogLevel minimum)
    {
        var (_, _, stderr) = await CaptureAsync(() =>
        {
            using var host = RunnerHost.Build(
                settings,
                new RunnerMountPlan { AllowExternalMounts = true },
                configureLogging: (_, builder) =>
                {
                    builder.ClearProviders();
                    builder.SetMinimumLevel(minimum);
                    builder.AddProvider(logs);
                });
            return Task.FromResult(0);
        });
        return stderr;
    }

    private static async Task<(int Exit, string Stdout, string Stderr)> CaptureAsync(Func<Task<int>> run)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter(new StringBuilder());
        using var stderr = new StringWriter(new StringBuilder());
        Console.SetOut(stdout);
        Console.SetError(stderr);
        try
        {
            var exit = await run();
            return (exit, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }
}
