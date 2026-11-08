using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Hosting.Tests.Doubles;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// FWK-28 (ADR-018, point 9): a YAML graph crew that loops through a decision node is interrupted and
/// resumed. The resumed run walks the graph again and takes the same path; the executions of a task that
/// already ended are answered from the checkpoint, under <c>&lt;task&gt;#&lt;n&gt;</c> by their order, so
/// a node entered three times is three entries and none is run again.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class GraphLoopResumeTests
{
    private static string Crew(string name) => $$"""
        name: "{{name}}"
        goal: "A graph that loops until its reviewer approves"
        process: "graph"
        graph:
          nodes:
            - { id: write,   task: write }
            - { id: review,  task: review }
            - { id: publish, task: publish }
          edges:
            - { from: write,   to: review }
            - { from: review,  to: write, when: { review.verdict: [revise] } }
            - { from: review,  to: publish }
            - { from: publish, to: END }
          maxVisits: 5
        agents:
          worker:
            role: "Worker"
            goal: "Work"
            backstory: "A minimal test agent."
            maxIter: 1
        tasks:
          write:
            description: "Write."
            expectedOutput: "A draft."
            agent: "worker"
          review:
            description: "Review."
            expectedOutput: "A verdict."
            decision:
              questions:
                verdict:
                  choice:
                    approve: "ship it"
                    revise: "send it back"
          publish:
            description: "Publish."
            expectedOutput: "A page."
            agent: "worker"
        """;

    private static FakeDecisionModel Reviewer() => new() { Choices = ["revise", "revise", "approve"] };

    private static Action<IServiceCollection> With(FakeDecisionModel model) =>
        services => services.AddSingleton<IDecisionModel>(model);

    [Fact]
    [Trait("BR", "FWK-28")]
    [Trait("BR", "FWK-07")]
    public async Task ShouldTakeTheSamePathAndAnswerEndedExecutionsFromTheCheckpoint_WhenALoopedRunIsResumed()
    {
        using var harness = new ResumeRunHarness();
        var crew = harness.WriteCrewYaml("crew", Crew(harness.CrewName));
        var ct = TestContext.Current.CancellationToken;

        // The run is cut after the decision node's second visit: the third write fails.
        var killed = new FakeAgentExecutionService();
        killed.FailOnCall["write"] = 3;
        // One model for both runs, answering revise, revise, approve in the order it is asked: an uninterrupted
        // run asks it three times. A decision that is asked again on resume would get "approve" at once and
        // take another path.
        var decisions = Reviewer();
        var first = await harness.RunAsync(crew, killed, configure: With(decisions), ct: ct);

        Assert.True(first.Exit == 2, $"exit={first.Exit} stderr={first.Stderr}");
        Assert.Equal(["write", "write", "write"], killed.Calls);
        Assert.Equal(2, decisions.Requests);

        var interrupted = await StateOfLatestRunAsync(harness);
        Assert.Equal(CheckpointStatus.Completed, interrupted.TaskCheckpoints["write#1"].Status);
        Assert.Equal(CheckpointStatus.Completed, interrupted.TaskCheckpoints["write#2"].Status);
        Assert.DoesNotContain("write#3", interrupted.TaskCheckpoints.Where(c => c.Value.Status == CheckpointStatus.Completed).Select(c => c.Key));

        var resumedService = new FakeAgentExecutionService();
        var resumed = await harness.RunAsync(crew, resumedService, resume: "latest", configure: With(decisions), ct: ct);

        Assert.True(resumed.Exit == 0, $"exit={resumed.Exit} stderr={resumed.Stderr}");
        // The two writes that ended are not run again: only the one that failed, then publish.
        Assert.Equal(["write", "publish"], resumedService.Calls);
        // The two decisions taken before the cut are replayed from the checkpoint, not asked again: the model
        // was asked as often in all as in an uninterrupted run (2 before the cut, 1 after), so the route is the same.
        Assert.Equal(3, decisions.Requests);
        var finished = await StateOfLatestRunAsync(harness);
        Assert.Equal(
            ["publish#1", "review#1", "review#2", "review#3", "write#1", "write#2", "write#3"],
            finished.TaskCheckpoints.Where(c => c.Value.Status == CheckpointStatus.Completed).Select(c => c.Key).Order(StringComparer.Ordinal).ToArray());
    }

    private static async Task<SessionState> StateOfLatestRunAsync(ResumeRunHarness harness)
    {
        var store = harness.Store();
        var latest = (await store.ListAsync(TestContext.Current.CancellationToken))
            .Where(s => s.CrewKey.StartsWith(harness.CrewName + "@", StringComparison.Ordinal))
            .OrderByDescending(s => s.UpdatedAt)
            .First();
        return (await store.GetAsync(latest.SessionId, TestContext.Current.CancellationToken))!;
    }
}
