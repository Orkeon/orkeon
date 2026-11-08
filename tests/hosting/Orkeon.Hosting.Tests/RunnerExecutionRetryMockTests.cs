using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Crew;
using Orkeon.Hosting.Tests.Doubles;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// FWK-15 through <c>--llm-mock</c>: a YAML crew whose task has <c>retry:</c>, run with the mock
/// provider. The mock answers a prompt the same way every time, so what it can show is a task whose
/// every attempt fails: the task is attempted exactly as often as it says, each attempt announced by a
/// <c>task.started</c> with its number, and the run then fails. (A task that fails first and then
/// succeeds is covered with a scripted execution service in <see cref="RetryAllModesTests"/>.)
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class RunnerExecutionRetryMockTests : IDisposable
{
    private sealed class TestOptions : RunnerOptionsBase { }

    private readonly string _root = Path.Combine(Path.GetTempPath(), "orkeon-hosting-retry-mock-" + Guid.NewGuid().ToString("N"));
    private string CrewDir => Path.Combine(_root, "crew");
    private string MockDir => Path.Combine(_root, "mock");

    public RunnerExecutionRetryMockTests()
    {
        Directory.CreateDirectory(CrewDir);
        Directory.CreateDirectory(MockDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* best-effort temp cleanup */ }
        catch (UnauthorizedAccessException) { /* best-effort temp cleanup */ }
    }

    private async Task<(int Exit, string Err, FakeCrewExecutionHook Hook)> RunAsync(string responseLine)
    {
        await File.WriteAllTextAsync(Path.Combine(CrewDir, "config.yaml"), """
            name: "retry-mock"
            goal: "Write one answer"
            process: "sequential"
            agents:
              scribe:
                role: "Scribe"
                goal: "Answer"
                backstory: "A minimal test agent."
                maxIter: 3
            tasks:
              answer_it:
                description: "Answer the question."
                expectedOutput: "An answer."
                agent: "scribe"
                retry: { attempts: 3 }
            """, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(CrewDir, "appsettings.json"), """{ "RaggableTree": { "Enabled": false } }""", TestContext.Current.CancellationToken);
        var responses = Path.Combine(MockDir, "answers.jsonl");
        await File.WriteAllTextAsync(responses, responseLine + "\n", TestContext.Current.CancellationToken);
        var console = new FakeRunnerConsole();
        var hook = new FakeCrewExecutionHook();
        var opts = new TestOptions
        {
            ConfigPath = Path.Combine(CrewDir, "config.yaml"),
            SettingsPath = Path.Combine(CrewDir, "appsettings.json"),
            AllowExternalMounts = true,
            LlmMock = responses,
            RunnerConsole = console,
        };

        var exit = await RunnerExecution.RunOneShotAsync(
            opts, "Orkeon.Hosting.Tests", (_, services) => services.AddScoped<ICrewExecutionHook>(_ => hook), TestContext.Current.CancellationToken);
        return (exit, console.ErrorOutput.ToString(), hook);
    }

    [Fact]
    [Trait("BR", "FWK-15")]
    public async Task ShouldAttemptAFailingTaskThreeTimes_ThenFailTheRun()
    {
        var (exit, err, hook) = await RunAsync("""{"match":"something the crew never asks","content":"unused"}""");

        Assert.Equal(2, exit);
        Assert.Contains("No line of /llm-mock/answers.jsonl matches the prompt", err, StringComparison.Ordinal);
        Assert.Equal([1, 2, 3], hook.Started.Select(s => s.Attempt).Order());
    }

    [Fact]
    [Trait("BR", "FWK-15")]
    public async Task ShouldRunASucceedingTaskOnce_WhateverItsRetry()
    {
        var (exit, err, hook) = await RunAsync("""{"match":"Answer the question","content":"Forty-two."}""");

        Assert.True(exit == 0, err);
        Assert.Equal([1], hook.Started.Select(s => s.Attempt));
    }
}
