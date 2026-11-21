using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

    private const string LadderSettings = """
        { "RaggableTree": { "Enabled": false },
          "Orkeon": { "Decision": { "Route": { "Ladder": [ "default", "{rung}" ] } } },
          "Llm": { "Provider": "mock", "Responses": "/crew/answers.jsonl",
                   "Profiles": { "real": { "BaseUrl": "https://api.anthropic.com/v1", "ApiKey": "sk-test", "Model": "claude-sonnet-5" } } } }
        """;

    /// <summary>A hierarchical crew whose manager, on <c>llm.profile: auto</c>, has no task of its own.</summary>
    private static string WriteManagerOnAutoCrew(string root, string rung, bool workerOnAuto)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "appsettings.json"), LadderSettings.Replace("{rung}", rung, StringComparison.Ordinal));
        File.WriteAllText(Path.Combine(root, "answers.jsonl"), "{\"match\":\"\",\"content\":\"ok\"}\n");
        var path = Path.Combine(root, "crew.yaml");
        File.WriteAllText(path, "name: led-crew\ngoal: Lead the work\nprocess: hierarchical\nmanagerAgent: chef\nagents:\n"
            + "  chef:\n    role: Chef\n    goal: Lead the team\n    llm:\n      profile: auto\n"
            + "  worker:\n    role: Worker\n    goal: Work\n    maxIter: 1\n" + (workerOnAuto ? "    llm:\n      profile: auto\n" : string.Empty)
            + "tasks:\n  work:\n    description: Work.\n    expected_output: Done.\n    agent: worker\n");
        return path;
    }

    [Theory]
    [InlineData("real", false, "Completed")]
    [InlineData("real", true, "Refused")]
    [InlineData("ghost", false, "Completed")]
    [Trait("BR", "CH-434")]
    public async Task ShouldResolveOnlyTheDefaultForAManagerOnAuto_WhenAnOfflineRunChecksItsProfiles(string rung, bool workerOnAuto, string expected)
    {
        var root = Path.Combine(Path.GetTempPath(), "orkeon-offline-" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = new FakeAgentExecutionService();
            using var entry = OpenOffline(WriteManagerOnAutoCrew(root, rung, workerOnAuto), service);

            var result = await entry.RunAsync(EmptyInput, resume: null, TestContext.Current.CancellationToken, offline: true);

            Assert.True(expected == result.Outcome.ToString(), $"{result.Outcome}: {result.Refusal} {result.LoadError?.Message}");
            if (expected == "Refused")
            {
                Assert.Contains("agent 'Worker' (profile 'real'", result.Refusal, StringComparison.Ordinal);
                Assert.DoesNotContain("Chef", result.Refusal, StringComparison.Ordinal);
            }
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { /* best-effort */ }
        }
    }

    private const string OverrideSettings = """
        { "RaggableTree": { "Enabled": false },
          "Orkeon": { "Decision": { "Route": { "Ladder": [ "default", "{rung}" ] } } },
          "Llm": { "Provider": "mock", "Responses": "/crew/answers.jsonl",
                   "Profiles": { "real": { "BaseUrl": "https://api.anthropic.com/v1", "ApiKey": "sk-test", "Model": "claude-sonnet-5" },
                                 "local": { "Provider": "mock", "Responses": "/crew/answers.jsonl" } } } }
        """;

    /// <summary>A crew whose agent runs on the default (the mock) and whose task overrides it with <paramref name="profile"/>.</summary>
    private static string WriteOverrideCrew(string root, string profile, string rung, bool decisionNone)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "appsettings.json"), OverrideSettings.Replace("{rung}", rung, StringComparison.Ordinal));
        File.WriteAllText(Path.Combine(root, "answers.jsonl"), "{\"match\":\"\",\"content\":\"ok\"}\n");
        var path = Path.Combine(root, "crew.yaml");
        File.WriteAllText(path, "name: override-crew\ngoal: Check the overrides\nprocess: sequential\n"
            + (decisionNone ? "decision:\n  mode: none\n" : string.Empty)
            + "agents:\n  worker:\n    role: Worker\n    goal: Work\n    maxIter: 1\n"
            + "tasks:\n  plain:\n    description: Plain.\n    expected_output: Done.\n    agent: worker\n"
            + "  moved:\n    description: Moved.\n    expected_output: Done.\n    agent: worker\n"
            + "    llm_override:\n      profile: " + profile + "\n");
        return path;
    }

    [Theory]
    [InlineData("real", "local", false, "Refused")]
    [InlineData("local", "local", false, "Completed")]
    [InlineData("default", "real", false, "Completed")]
    [InlineData("auto", "real", false, "Refused")]
    [InlineData("auto", "local", false, "Completed")]
    [InlineData("auto", "real", true, "LoadFailed")]
    [Trait("BR", "CH-434")]
    public async Task ShouldCheckTheProfileATaskOverrideRunsOn_WhenAnOfflineRunChecksItsProfiles(
        string profile, string rung, bool decisionNone, string expected)
    {
        var root = Path.Combine(Path.GetTempPath(), "orkeon-offline-" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = new FakeAgentExecutionService();
            using var entry = OpenOffline(WriteOverrideCrew(root, profile, rung, decisionNone), service);

            var result = await entry.RunAsync(EmptyInput, resume: null, TestContext.Current.CancellationToken, offline: true);

            Assert.True(expected == result.Outcome.ToString(), $"{result.Outcome}: {result.Refusal} {result.LoadError?.Message}");
            if (expected == "Refused")
            {
                Assert.Contains("task 'moved' (profile 'real'", result.Refusal, StringComparison.Ordinal);
                Assert.DoesNotContain("plain", result.Refusal, StringComparison.Ordinal);
                Assert.DoesNotContain("agent 'Worker'", result.Refusal, StringComparison.Ordinal);
                Assert.Empty(service.Calls);
            }
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { /* best-effort */ }
        }
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async Task ShouldFailCleanlyNamingTheTask_WhenAnOfflineRunsTaskOverrideNamesAnUnknownProfile()
    {
        var root = Path.Combine(Path.GetTempPath(), "orkeon-offline-" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = new FakeAgentExecutionService();
            using var entry = OpenOffline(WriteOverrideCrew(root, "ghost", "local", decisionNone: false), service);

            var result = await entry.RunAsync(EmptyInput, resume: null, TestContext.Current.CancellationToken, offline: true);

            Assert.True(result.Outcome is CrewRunOutcome.LoadFailed or CrewRunOutcome.Refused, $"{result.Outcome}");
            var message = result.Refusal ?? result.LoadError?.Message;
            Assert.NotNull(message);
            Assert.Contains("'ghost'", message, StringComparison.Ordinal);
            Assert.Contains("Moved", message, StringComparison.Ordinal);
            Assert.Empty(service.Calls);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { /* best-effort */ }
        }
    }

    private const string SkillsPickFault = "the decision model is down";

    /// <summary>
    /// A crew of three tasks whose agent has System One pick its skills from a library of one skill, run by the real
    /// agent loop on the mock model.
    /// </summary>
    private static string WriteSkillsAutoCrew(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "skills"));
        File.WriteAllText(Path.Combine(root, "skills", "style.md"), "Style: write plainly.\n");
        File.WriteAllText(Path.Combine(root, "appsettings.json"), """
            { "RaggableTree": { "Enabled": false },
              "Orkeon": { "Skills": { "Library": "/crew/skills" } },
              "Llm": { "Provider": "mock", "Responses": "/crew/answers.jsonl" } }
            """);
        File.WriteAllText(Path.Combine(root, "answers.jsonl"), "{\"match\":\"\",\"content\":\"ok\"}\n");
        var path = Path.Combine(root, "crew.yaml");
        File.WriteAllText(path, "name: skilled-crew\ngoal: Write three parts\nprocess: sequential\nagents:\n"
            + "  worker:\n    role: Worker\n    goal: Work\n    maxIter: 1\n    skills:\n      auto: true\n"
            + "tasks:\n"
            + string.Concat(Enumerable.Range(1, 3).Select(i => $"  part{i}:\n    description: Write part {i}.\n    expected_output: Done.\n    agent: worker\n")));
        return path;
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async Task ShouldWarnOfAFailedSkillsPickOncePerRun_WhenOneHostRunsACrewOfThreeTasksTwice()
    {
        var root = Path.Combine(Path.GetTempPath(), "orkeon-skills-" + Guid.NewGuid().ToString("N"));
        try
        {
            // Arrange
            using var logs = new CapturingLoggerProvider();
            var model = new FakeDecisionModel { Fault = SkillsPickFault };
            var observer = new FakeAgentSkillsObserver();
            var opts = new Options
            {
                ConfigPath = WriteSkillsAutoCrew(root), AllowExternalMounts = true, WorkingDirectory = root, RunnerConsole = new FakeRunnerConsole(),
            };
            Assert.True(CrewRunEntryPoint.TryOpen(
                opts,
                "Orkeon.Hosting.Tests",
                (_, services) =>
                {
                    services.AddSingleton<Orkeon.Application.Interfaces.Ports.IDecisionModel>(model);
                    services.AddSingleton<Orkeon.Application.Interfaces.Ports.IAgentSkillsObserver>(observer);
                },
                out var opened,
                out var code,
                (_, logging) => logging.AddProvider(logs)), $"TryOpen refused with exit {code}");
            using var entry = opened!;
            int PickFailedWarnings() => logs.Entries.Count(e => e.Level == LogLevel.Warning
                && e.Message.Contains("could not pick its skills", StringComparison.Ordinal));

            // Act
            var first = await entry.RunAsync(EmptyInput, resume: null, TestContext.Current.CancellationToken);
            var afterFirst = PickFailedWarnings();
            var noticesOfFirst = observer.Notices.Count;
            var second = await entry.RunAsync(EmptyInput, resume: null, TestContext.Current.CancellationToken);

            // Assert: three failed picks in each run, one warning in each, the detail on every task's agent.skills.
            Assert.True(first.Output?.Succeeded, first.Output?.Error ?? first.LoadError?.Message);
            Assert.True(second.Output?.Succeeded, second.Output?.Error ?? second.LoadError?.Message);
            Assert.Equal(6, model.Requests);
            Assert.Equal(1, afterFirst);
            Assert.Equal(2, PickFailedWarnings());
            Assert.Equal(3, noticesOfFirst);
            Assert.Equal(6, observer.Notices.Count);
            Assert.All(observer.Notices, n =>
            {
                Assert.Equal(Orkeon.Application.Interfaces.Ports.AgentSkillsReasons.PickFailed, n.Reason);
                Assert.Contains(SkillsPickFault, n.Detail, StringComparison.Ordinal);
            });
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { /* best-effort */ }
        }
    }
}
