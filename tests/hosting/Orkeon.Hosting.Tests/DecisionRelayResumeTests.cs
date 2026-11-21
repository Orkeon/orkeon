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
              analyst: { role: "Analyst", goal: "Judge the ticket", backstory: "A minimal test agent.", maxIter: 1 }
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

    [Fact]
    [Trait("BR", "CH-430")]
    public async Task ShouldAskTheRelayAgentByItsName_WhenAScriptedCrewRunsWithTheCircuitOffAndTheRoleDiffers()
    {
        // Arrange
        using var harness = new ResumeRunHarness();
        var crew = harness.WriteCrewScript("crew", $$"""
            /// <reference orkeon-script="1.0" />
            const analyst = agentBuilder().name("analyst").role("Analyst").goal("Judge the ticket").build();
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
            run.Exit == 0 && circuit.Relay.Calls == 1 && decision.DecisionSource == DecisionSources.Relay && decision.Success,
            $"exit={run.Exit} relay={circuit.Relay.Calls} source={decision.DecisionSource} stderr={run.Stderr}");
    }
}
