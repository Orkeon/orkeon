using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel;
using Orkeon.Hosting.Tests.Doubles;
using Orkeon.Scripting.Runtime;

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
                mounts: new RunnerMountPlan(),
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
    public async Task MissingLlmSection_RunsOnTheEchoProvider()
    {
        // The warning's promise, kept: no section means the echo provider, which replays the
        // prompt — not the infrastructure's keyless OpenAI default, whose refusal is a failed
        // task since LLM-11 (the bundled demos exited 2 where the warning announced a run).
        using var host = CaptureStderr(() => RunnerHost.Build(
            settingsPath: null,
            mounts: new RunnerMountPlan(),
            configureLogging: (_, b) => b.SetMinimumLevel(LogLevel.None)));

        Assert.IsType<UndefinedLlmProvider>(host.Services.GetRequiredService<ILlmProvider>());
        var ct = TestContext.Current.CancellationToken;
        Assert.Equal("hello", await host.Services.GetRequiredService<IBasicLlmProvider>().ChatAsync("hello", cancellationToken: ct));
        var answer = await host.Services.GetRequiredService<IChatClient>().GetResponseAsync("hello", cancellationToken: ct);
        Assert.Equal("hello", answer.Text);
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
                mounts: new RunnerMountPlan(),
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

    /// <summary>Builds under a muted stderr (the fallback warning is another test's subject) and hands the result back.</summary>
    private static T CaptureStderr<T>(Func<T> action)
    {
        var original = Console.Error;
        using var writer = new StringWriter();
        Console.SetError(writer);
        try
        {
            return action();
        }
        finally
        {
            Console.SetError(original);
        }
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
