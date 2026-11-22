using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Constants.Protocol;
using Orkeon.Hosting.Tests.Doubles;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// #430: an <c>optional</c> crew whose decision task is answered by the decision model (circuit on) or by its
/// <c>without:</c> relay (circuit off), run from its file in each process. The decision is recorded once and
/// replayed on resume with its source; a run resumed under the other circuit is refused.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class DecisionRelayResumeTests
{
    private const string RelayAnswer = """{"route":{"bug":0.9,"other":0.1}}""";

    private static string Crew(string name, string process)
    {
        var graph = process != "graph" ? "" : """
            graph:
              nodes:
                - { id: intake, task: intake }
                - { id: triage, task: triage }
                - { id: answer, task: answer }
              edges:
                - { from: intake, to: triage }
                - { from: triage, to: answer, when: { triage.route: [bug] } }
                - { from: triage, to: END }
                - { from: answer, to: END }
            """;
        var answerWhen = process == "graph" ? "" : """
                dependencies: ["triage"]
                when: { triage.route: [bug] }
            """;
        var triageAfter = process == "graph" ? "" : """
                dependencies: ["intake"]
            """;
        return $$"""
            name: "{{name}}"
            goal: "Triage a ticket"
            process: "{{process}}"
            decision: { mode: optional }
            {{graph}}
            agents:
              worker: { role: "Worker", goal: "Work", backstory: "A minimal test agent.", maxIter: 1 }
              analyst: { role: "Triage lead", goal: "Judge the ticket", backstory: "A minimal test agent.", maxIter: 1 }
            tasks:
              intake:
                description: "Read the ticket."
                expectedOutput: "A summary."
                agent: "worker"
              triage:
                description: "Route the ticket."
                expectedOutput: "A route."
            {{triageAfter}}
                decision:
                  questions:
                    route:
                      choice:
                        bug: "a defect"
                        other: "anything else"
                  without:
                    agent: analyst
              answer:
                description: "Answer the ticket."
                expectedOutput: "An answer."
                agent: "worker"
            {{answerWhen}}
            """;
    }

    private sealed record Circuit(FakeDecisionModel Model, FakeDecisionRelay Relay, bool On)
    {
        public int Asked => On ? Model.Requests : Relay.Calls;

        public Action<IServiceCollection> Configure => services =>
        {
            services.AddSingleton<IDecisionModel>(Model);
            services.AddSingleton<IDecisionRelay>(Relay);
            if (!On)
                services.AddSingleton(DecisionCircuit.OffBy(DecisionCircuitCause.Settings));
        };

        public static Circuit Of(bool on) =>
            new(new FakeDecisionModel { Choices = ["bug"] }, new FakeDecisionRelay { Answer = RelayAnswer, Tokens = 50 }, on);
    }

    public static TheoryData<string, bool> Processes => new()
    {
        { "sequential", true }, { "sequential", false },
        { "parallel", true }, { "parallel", false },
        { "graph", true }, { "graph", false },
    };

    [Theory]
    [MemberData(nameof(Processes))]
    [Trait("BR", "CH-430")]
    [Trait("BR", "FWK-28")]
    public async Task ShouldReplayTheRecordedDecisionWithItsSourceAndNoNewCall_WhenTheRunIsResumed(string process, bool on)
    {
        // Arrange
        using var harness = new ResumeRunHarness();
        var crew = harness.WriteCrewYaml("crew", Crew(harness.CrewName, process));
        var ct = TestContext.Current.CancellationToken;
        var circuit = Circuit.Of(on);
        var killed = new FakeAgentExecutionService();
        killed.FailOnCall["answer"] = 1;
        var first = await harness.RunAsync(crew, killed, configure: circuit.Configure, ct: ct);
        Assert.True(first.Exit == 2 && circuit.Asked == 1, $"exit={first.Exit} asked={circuit.Asked} stderr={first.Stderr}");
        var hook = new FakeCrewExecutionHook();

        // Act
        var resumed = await harness.RunAsync(crew, new FakeAgentExecutionService(), resume: "latest", hook: hook, configure: circuit.Configure, ct: ct);

        // Assert
        var decision = hook.Completed.Single(s => s.IsDecision);
        Assert.Equal(
            (0, 1, on ? DecisionSources.Model : DecisionSources.Relay, true),
            (resumed.Exit, circuit.Asked, decision.DecisionSource, decision.Success));
    }

    [Theory]
    [InlineData("sequential")]
    [InlineData("parallel")]
    [InlineData("graph")]
    [Trait("BR", "CH-430")]
    public async Task ShouldFollowTheRelayAnswerAndReportItsSourceAndTokens_WhenTheCircuitIsOff(string process)
    {
        // Arrange
        using var harness = new ResumeRunHarness();
        var crew = harness.WriteCrewYaml("crew", Crew(harness.CrewName, process));
        var circuit = Circuit.Of(on: false);
        var service = new FakeAgentExecutionService();
        var hook = new FakeCrewExecutionHook();

        // Act
        var run = await harness.RunAsync(crew, service, hook: hook, configure: circuit.Configure, ct: TestContext.Current.CancellationToken);

        // Assert
        var decision = hook.Completed.Single(s => s.IsDecision);
        Assert.Equal(
            (0, DecisionSources.Relay, false, 100, true, 0, true),
            (run.Exit, decision.DecisionSource, decision.Defaulted, decision.TokensUsed, service.Calls.Contains("answer"), circuit.Model.Requests,
                run.Stdout.Contains("Tokens used: 100", StringComparison.Ordinal)));
    }

    [Fact]
    [Trait("BR", "CH-451")]
    public async Task ShouldFollowTheRelay_WhenTheHostedDecisionProviderHasNoKey()
    {
        // Arrange: a Liquid section whose key variable no machine sets, and no key setting.
        using var harness = new ResumeRunHarness();
        var settings = """{ "RaggableTree": { "Enabled": false }, "Orkeon": { "Liquid": { "Model": "lfm", "ApiKeyEnvVar": "ORKEON_TEST_451_NO_SUCH_KEY" } } }""";
        var crew = harness.WriteCrewYaml("crew", Crew(harness.CrewName, "sequential"), settings);
        var relay = new FakeDecisionRelay { Answer = RelayAnswer, Tokens = 50 };
        var service = new FakeAgentExecutionService();
        var hook = new FakeCrewExecutionHook();

        // Act
        var run = await harness.RunAsync(
            crew, service, hook: hook, configure: services => services.AddSingleton<IDecisionRelay>(relay), ct: TestContext.Current.CancellationToken);

        // Assert
        var decision = hook.Completed.Single(s => s.IsDecision);
        Assert.True(
            run.Exit == 0 && decision.DecisionSource == DecisionSources.Relay && relay.Calls == 1 && service.Calls.Contains("answer"),
            $"exit={run.Exit} source={decision.DecisionSource} relay={relay.Calls} stderr={run.Stderr}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("BR", "CH-430")]
    public async Task ShouldRefuseTheResume_WhenAnOptionalCrewIsResumedUnderTheOtherCircuit(bool startedOn)
    {
        // Arrange
        using var harness = new ResumeRunHarness();
        var crew = harness.WriteCrewYaml("crew", Crew(harness.CrewName, "sequential"));
        var ct = TestContext.Current.CancellationToken;
        var killed = new FakeAgentExecutionService();
        killed.FailOnCall["answer"] = 1;
        await harness.RunAsync(crew, killed, configure: Circuit.Of(startedOn).Configure, ct: ct);
        var resumedService = new FakeAgentExecutionService();

        // Act
        var resumed = await harness.RunAsync(crew, resumedService, resume: "latest", configure: Circuit.Of(!startedOn).Configure, ct: ct);

        // Assert
        Assert.True(
            resumed.Exit != 0 && resumedService.Calls.Count == 0
                && resumed.Stderr.Contains($"was started with the decision circuit {(startedOn ? "on" : "off")}", StringComparison.Ordinal),
            $"exit={resumed.Exit} calls={resumedService.Calls.Count} stderr={resumed.Stderr}");
    }

    [Theory]
    [InlineData("parallel", true)]
    [InlineData("parallel", false)]
    [InlineData("graph", true)]
    [InlineData("graph", false)]
    [Trait("BR", "CH-430")]
    public async Task ShouldRefuseTheResume_WhenAParallelOrGraphCrewIsResumedUnderTheOtherCircuit(string process, bool startedOn)
    {
        // Arrange
        using var harness = new ResumeRunHarness();
        var crew = harness.WriteCrewYaml("crew", Crew(harness.CrewName, process));
        var ct = TestContext.Current.CancellationToken;
        var killed = new FakeAgentExecutionService();
        killed.FailOnCall["answer"] = 1;
        await harness.RunAsync(crew, killed, configure: Circuit.Of(startedOn).Configure, ct: ct);
        var resumedService = new FakeAgentExecutionService();

        // Act
        var resumed = await harness.RunAsync(crew, resumedService, resume: "latest", configure: Circuit.Of(!startedOn).Configure, ct: ct);

        // Assert
        Assert.True(
            resumed.Exit != 0 && resumedService.Calls.Count == 0
                && resumed.Stderr.Contains($"was started with the decision circuit {(startedOn ? "on" : "off")}", StringComparison.Ordinal),
            $"exit={resumed.Exit} calls={resumedService.Calls.Count} stderr={resumed.Stderr}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("BR", "CH-430")]
    public async Task ShouldExitOneWithTheMismatchRefusal_WhenAnOptionalCrewIsResumedUnderTheOtherCircuit(bool startedOn)
    {
        // Arrange
        using var harness = new ResumeRunHarness();
        var crew = harness.WriteCrewYaml("crew", Crew(harness.CrewName, "sequential"));
        var ct = TestContext.Current.CancellationToken;
        var killed = new FakeAgentExecutionService();
        killed.FailOnCall["answer"] = 1;
        await harness.RunAsync(crew, killed, configure: Circuit.Of(startedOn).Configure, ct: ct);

        // Act
        var resumed = await harness.RunAsync(crew, new FakeAgentExecutionService(), resume: "latest", configure: Circuit.Of(!startedOn).Configure, ct: ct);

        // Assert
        Assert.True(
            resumed.Exit == 1 && resumed.Stderr.Contains("start a new run", StringComparison.Ordinal),
            $"exit={resumed.Exit} stderr={resumed.Stderr}");
    }

    [Fact]
    [Trait("BR", "CH-430")]
    public async Task ShouldRefuseTheResumeWithTheMismatch_WhenAnOptionalCrewRecordedWithSystemOneIsResumedWithoutADecisionModel()
    {
        // Arrange
        using var harness = new ResumeRunHarness();
        var crew = harness.WriteCrewYaml("crew", Crew(harness.CrewName, "sequential"));
        var ct = TestContext.Current.CancellationToken;
        var killed = new FakeAgentExecutionService();
        killed.FailOnCall["answer"] = 1;
        await harness.RunAsync(crew, killed, configure: Circuit.Of(on: true).Configure, ct: ct);
        var resumedService = new FakeAgentExecutionService();

        // Act
        var resumed = await harness.RunAsync(crew, resumedService, resume: "latest", configure: WithoutDecisionModel, ct: ct);

        // Assert
        Assert.True(
            resumed.Exit == 1 && resumedService.Calls.Count == 0
                && resumed.Stderr.Contains("was started with the decision circuit on", StringComparison.Ordinal),
            $"exit={resumed.Exit} calls={resumedService.Calls.Count} stderr={resumed.Stderr}");
    }

    [Fact]
    [Trait("BR", "CH-430")]
    public async Task ShouldResume_WhenACrewWithModeNoneRecordedWithSystemOneIsResumedWithoutADecisionModel()
    {
        // Arrange
        using var harness = new ResumeRunHarness();
        var crew = harness.WriteCrew("crew", "sequential", extra: "decision: { mode: none }");
        var ct = TestContext.Current.CancellationToken;
        var killed = new FakeAgentExecutionService();
        killed.FailOnCall["third"] = 1;
        await harness.RunAsync(crew, killed, configure: Circuit.Of(on: true).Configure, ct: ct);
        var resumedService = new FakeAgentExecutionService();

        // Act
        var resumed = await harness.RunAsync(crew, resumedService, resume: "latest", configure: WithoutDecisionModel, ct: ct);

        // Assert
        Assert.True(
            resumed.Exit == 0 && resumedService.Calls.SequenceEqual(["third"]),
            $"exit={resumed.Exit} calls={string.Join(",", resumedService.Calls)} stderr={resumed.Stderr}");
    }

    [Theory]
    [InlineData("sequential")]
    [InlineData("parallel")]
    [InlineData("graph")]
    [Trait("BR", "CH-430")]
    public async Task ShouldDecideAgain_WhenTheDecisionTaskChangedSinceTheRunAndTheRunIsResumed(string process)
    {
        // Arrange
        using var harness = new ResumeRunHarness();
        var yaml = Crew(harness.CrewName, process);
        var crew = harness.WriteCrewYaml("crew", yaml);
        var ct = TestContext.Current.CancellationToken;
        var circuit = Circuit.Of(on: true);
        var killed = new FakeAgentExecutionService();
        killed.FailOnCall["answer"] = 1;
        await harness.RunAsync(crew, killed, configure: circuit.Configure, ct: ct);
        harness.WriteCrewYaml("crew", yaml.Replace("other: \"anything else\"", "other: \"a question or a feature request\"", StringComparison.Ordinal));

        // Act
        var resumed = await harness.RunAsync(crew, new FakeAgentExecutionService(), resume: "latest", configure: circuit.Configure, ct: ct);

        // Assert
        Assert.True(
            resumed.Exit == 0 && circuit.Model.Requests == 2,
            $"exit={resumed.Exit} requests={circuit.Model.Requests} stderr={resumed.Stderr}");
    }

    /// <summary>The circuit on in the settings and no decision model registered: the runner's circuit is off (#430).</summary>
    private static void WithoutDecisionModel(IServiceCollection services) => services.AddSingleton(DecisionCircuit.On);

    [Fact]
    [Trait("BR", "CH-430")]
    public async Task ShouldAskTheRelayAgentByItsName_WhenAScriptedCrewRunsWithTheCircuitOffAndTheRoleDiffers()
    {
        // Arrange
        using var harness = new ResumeRunHarness();
        var crew = harness.WriteCrewScript("crew", $$"""
            /// <reference orkeon-script="1.0" />
            const analyst = agentBuilder().name("analyst").role("Triage lead").goal("Judge the ticket").build();
            const worker = agentBuilder().name("worker").role("Worker").goal("Work").build();
            const intake = taskBuilder().name("intake").agent(worker).description("Read the ticket.").expectedOutput("A summary.").build();
            const triage = taskBuilder().name("triage").decision({
                questions: { route: { choice: { bug: "a defect", other: "anything else" } } },
                state: ["intake"],
                without: { agent: "analyst" },
            }).build();
            const crew = crewBuilder().name("{{harness.CrewName}}").goal("Triage a ticket").decision({ mode: "optional" })
                .withAgent(analyst).withAgent(worker).withTask(intake).withTask(triage).build();
            (globalThis as any).crew = crew;
            """);
        var circuit = Circuit.Of(on: false);
        var hook = new FakeCrewExecutionHook();

        // Act
        var run = await harness.RunAsync(crew, new FakeAgentExecutionService(), hook: hook, configure: circuit.Configure, ct: TestContext.Current.CancellationToken);

        // Assert
        var decision = hook.Completed.Single(s => s.IsDecision);
        Assert.True(
            run.Exit == 0 && circuit.Relay.Roles.SequenceEqual(["Triage lead"]) && decision.DecisionSource == DecisionSources.Relay && decision.Success,
            $"exit={run.Exit} relay={string.Join(",", circuit.Relay.Roles)} source={decision.DecisionSource} stderr={run.Stderr}");
    }
}
