using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Hosting.Tests.Doubles;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// FWK-09 - the entry point that loads a crew, kicks it off inside one scope and hands the final
/// output back: what <c>orkeon eval run --crew</c> reads once per case and <c>orkeon run</c> prints.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class CrewRunEntryPointTests
{
    private sealed class Options : RunnerOptionsBase { }

    /// <summary>A scoped service resolved inside the run: disposed with the scope that held it.</summary>
    private sealed class ScopeProbe : IAsyncDisposable
    {
        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private static CrewRunEntryPoint Open(string crewPath, FakeAgentExecutionService service, List<ScopeProbe> probes)
    {
        var opts = new Options { ConfigPath = crewPath, AllowExternalMounts = true, RunnerConsole = new FakeRunnerConsole() };
        Assert.True(CrewRunEntryPoint.TryOpen(
            opts,
            "Orkeon.Hosting.Tests",
            (_, services) =>
            {
                services.AddScoped<ScopeProbe>();
                services.AddScoped<IAgentExecutionService>(sp =>
                {
                    probes.Add(sp.GetRequiredService<ScopeProbe>());
                    return service;
                });
                services.AddScoped<IManagerAgent>(_ => new StubAcceptingManagerAgent());
            },
            out var entry,
            out var code), $"TryOpen refused with exit {code}");
        return entry!;
    }

    private static Task<CrewInput?> EmptyInput(Domain.Crew.Crew crew, CancellationToken ct) =>
        Task.FromResult<CrewInput?>(CrewInput.Empty());

    [Fact]
    [Trait("BR", "FWK-09")]
    public async Task ShouldReturnTheFinalOutput_WhenTheCrewRuns()
    {
        using var harness = new ResumeRunHarness();
        var crew = harness.WriteCrew("crew", "autonomous");
        using var entry = Open(crew, new FakeAgentExecutionService(), []);

        var result = await entry.RunAsync(EmptyInput, resume: null, TestContext.Current.CancellationToken);

        Assert.Equal(CrewRunOutcome.Completed, result.Outcome);
        Assert.NotNull(result.Output);
        Assert.True(result.Output.Succeeded, result.Output.Error);
        Assert.Contains("out:third", result.Output.FinalOutput, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-09")]
    public async Task ShouldReleaseTheScopeOfEveryRun_WhenTheCrewIsRunTwiceOverOneHost()
    {
        using var harness = new ResumeRunHarness();
        var crew = harness.WriteCrew("crew", "autonomous");
        var probes = new List<ScopeProbe>();
        using var entry = Open(crew, new FakeAgentExecutionService(), probes);

        await entry.RunAsync(EmptyInput, resume: null, TestContext.Current.CancellationToken);
        var afterFirst = probes.ToList();
        await entry.RunAsync(EmptyInput, resume: null, TestContext.Current.CancellationToken);

        Assert.NotEmpty(afterFirst);
        Assert.All(afterFirst, p => Assert.True(p.Disposed, "the first run's scope outlived its run"));
        Assert.NotEmpty(probes.Except(afterFirst));
        Assert.All(probes, p => Assert.True(p.Disposed, "a run's scope outlived the run"));
        Assert.Empty(afterFirst.Intersect(probes.Except(afterFirst)));
    }

    [Fact]
    [Trait("BR", "FWK-09")]
    public async Task ShouldReportALoadFailure_WithoutRunningAnyTask()
    {
        using var harness = new ResumeRunHarness();
        var crew = harness.WriteCrew("crew", "autonomous", extra: "tasks: [");
        var service = new FakeAgentExecutionService();
        using var entry = Open(crew, service, []);

        var result = await entry.RunAsync(EmptyInput, resume: null, TestContext.Current.CancellationToken);

        Assert.Equal(CrewRunOutcome.LoadFailed, result.Outcome);
        Assert.NotNull(result.LoadError);
        Assert.Empty(service.Calls);
    }

    [Fact]
    [Trait("BR", "FWK-09")]
    public async Task ShouldRunNothing_WhenBeforeKickoffReturnsNoInput()
    {
        using var harness = new ResumeRunHarness();
        var crew = harness.WriteCrew("crew", "autonomous");
        var service = new FakeAgentExecutionService();
        using var entry = Open(crew, service, []);

        var result = await entry.RunAsync((_, _) => Task.FromResult<CrewInput?>(null), resume: null, TestContext.Current.CancellationToken);

        Assert.Equal(CrewRunOutcome.Aborted, result.Outcome);
        Assert.Empty(service.Calls);
    }

    private const string OfflineSettings = """
        { "RaggableTree": { "Enabled": false },
          "Llm": { "Provider": "mock", "Responses": "/crew/answers.jsonl",
                   "Profiles": { "real": { "BaseUrl": "https://api.anthropic.com/v1", "ApiKey": "sk-test", "Model": "claude-sonnet-5" } } } }
        """;

    private static string WriteOfflineCrew(string root, bool agentOnRealProfile)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "appsettings.json"), OfflineSettings);
        File.WriteAllText(Path.Combine(root, "answers.jsonl"), "{\"match\":\"\",\"content\":\"ok\"}\n");
        var llm = agentOnRealProfile ? "    llm:\n      profile: real\n" : string.Empty;
        var path = Path.Combine(root, "crew.yaml");
        File.WriteAllText(path, "name: offline-crew\ngoal: Check the providers\nprocess: sequential\nagents:\n  worker:\n"
            + "    role: Worker\n    goal: Work\n    backstory: A minimal agent.\n    maxIter: 1\n" + llm
            + "tasks:\n  work:\n    description: Work.\n    expected_output: Done.\n    agent: worker\n");
        return path;
    }

    private static CrewRunEntryPoint OpenOffline(string crewPath, FakeAgentExecutionService service)
    {
        var opts = new Options { ConfigPath = crewPath, AllowExternalMounts = true, RunnerConsole = new FakeRunnerConsole() };
        Assert.True(CrewRunEntryPoint.TryOpen(
            opts,
            "Orkeon.Hosting.Tests",
            (_, services) =>
            {
                services.AddScoped<IAgentExecutionService>(_ => service);
                services.AddScoped<IManagerAgent>(_ => new StubAcceptingManagerAgent());
            },
            out var entry,
            out var code), $"TryOpen refused with exit {code}");
        return entry!;
    }

    [Theory]
    [InlineData(true, true, "Refused")]
    [InlineData(false, true, "Completed")]
    [InlineData(true, false, "Completed")]
    [Trait("BR", "FWK-09")]
    public async Task ShouldRefuseAnOfflineRunOnARealProfile_AndOnlyThen(bool offline, bool agentOnRealProfile, string expected)
    {
        var root = Path.Combine(Path.GetTempPath(), "orkeon-offline-" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = new FakeAgentExecutionService();
            using var entry = OpenOffline(WriteOfflineCrew(root, agentOnRealProfile), service);

            var result = await entry.RunAsync(EmptyInput, resume: null, TestContext.Current.CancellationToken, offline);

            Assert.Equal(expected, result.Outcome.ToString());
            if (expected == "Refused")
            {
                Assert.Contains("Worker", result.Refusal, StringComparison.Ordinal);
                Assert.Contains("real", result.Refusal, StringComparison.Ordinal);
                Assert.Empty(service.Calls);
            }
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { /* best-effort */ }
        }
    }
}
