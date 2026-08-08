using Microsoft.Extensions.Logging;
using Orkeon.Hosting.Tests.Doubles;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// Serialises the tests that redirect the process-global <see cref="Console"/> streams:
/// they must not run in parallel with anything else writing to stderr.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ConsoleSerialCollection
{
    public const string Name = "hosting-console-serial";
}

/// <summary>
/// WIN-01 — building a runner host without an <c>Llm</c> section must emit one explicit,
/// actionable warning (logger + stderr) instead of silently falling back to the echo
/// provider; a host with a configured <c>Llm</c> section must stay quiet.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class RunnerHostLlmFallbackWarningTests : IDisposable
{
    private const string Marker = "No `Llm` section configured";

    private readonly string _root;

    public RunnerHostLlmFallbackWarningTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "orkeon-llm-warning-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    [Fact]
    public void MissingLlmSection_EmitsWarning_ExactlyOnce_OnLoggerAndStderr()
    {
        using var logs = new CapturingLoggerProvider();
        var stderr = CaptureStderr(() =>
        {
            using var host = RunnerHost.Build(
                settingsPath: null,
                cliMounts: [],
                configureLogging: (_, b) =>
                {
                    b.AddProvider(logs);
                    b.SetMinimumLevel(LogLevel.Warning);
                });
        });

        // stderr is the guarantee channel — exactly one emission per host build.
        Assert.Equal(1, CountOccurrences(stderr, Marker));
        Assert.Contains("orkeon init", stderr, StringComparison.Ordinal);
        Assert.Contains("<undefined-llm>", stderr, StringComparison.Ordinal);
        Assert.Contains("ORKEON_Llm__BaseUrl", stderr, StringComparison.Ordinal);

        // And exactly one logger warning.
        var warnings = logs.Entries
            .Where(e => e.Level == LogLevel.Warning
                && e.Message.Contains(Marker, StringComparison.Ordinal))
            .ToList();
        Assert.Single(warnings);
        Assert.Contains("orkeon init", warnings[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfiguredLlmSection_EmitsNoWarning()
    {
        var settingsPath = Path.Combine(_root, "appsettings.json");
        File.WriteAllText(settingsPath,
            """{ "Llm": { "Model": "llama3.2", "BaseUrl": "http://localhost:11434" } }""");

        using var logs = new CapturingLoggerProvider();
        var stderr = CaptureStderr(() =>
        {
            using var host = RunnerHost.Build(
                settingsPath: settingsPath,
                cliMounts: [],
                configureLogging: (_, b) =>
                {
                    b.AddProvider(logs);
                    b.SetMinimumLevel(LogLevel.Warning);
                });
        });

        Assert.DoesNotContain(Marker, stderr, StringComparison.Ordinal);
        Assert.DoesNotContain(logs.Entries, e => e.Message.Contains(Marker, StringComparison.Ordinal));
    }

    private static string CaptureStderr(Action action)
    {
        var original = Console.Error;
        using var writer = new StringWriter();
        Console.SetError(writer);
        try
        {
            action();
        }
        finally
        {
            Console.SetError(original);
        }
        return writer.ToString();
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        for (var index = haystack.IndexOf(needle, StringComparison.Ordinal);
             index >= 0;
             index = haystack.IndexOf(needle, index + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }
}
